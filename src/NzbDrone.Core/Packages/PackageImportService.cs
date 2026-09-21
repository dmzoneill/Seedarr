using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Packages;

public class PackageImportService : IPackageImportService
{
    private const int BufferSize = 65536; // 64 KiB
    private const long DefaultMaxUncompressedBytes = 50L * 1024 * 1024 * 1024; // 50 GB

    private readonly ITorrentService _torrentService;
    private readonly ITorrentImportService _torrentImportService;
    private readonly IFastResumeService _fastResumeService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IDiskProvider _diskProvider;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly Logger _logger;

    public PackageImportService(
        ITorrentService torrentService = null,
        ITorrentImportService torrentImportService = null,
        IFastResumeService fastResumeService = null,
        IAppFolderInfo appFolderInfo = null,
        IDiskProvider diskProvider = null,
        ITrackerEntryService trackerEntryService = null)
    {
        _torrentService = torrentService;
        _torrentImportService = torrentImportService;
        _fastResumeService = fastResumeService;
        _appFolderInfo = appFolderInfo;
        _diskProvider = diskProvider;
        _trackerEntryService = trackerEntryService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<PackageImportResult> ImportPackageAsync(
        Stream archiveStream,
        PackageImportOptions options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        options ??= new PackageImportOptions();

        var isTemporarySandbox = false;
        var targetRootDir = options.TargetRootDir;

        if (string.IsNullOrWhiteSpace(targetRootDir))
        {
            var appData = _appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory;
            targetRootDir = Path.Combine(appData, "temp", "import_" + Guid.NewGuid().ToString("N"));
            isTemporarySandbox = true;
        }

        var canonicalTargetRoot = Path.GetFullPath(targetRootDir);
        Directory.CreateDirectory(canonicalTargetRoot);

        var effectiveMaxBytes = options.MaxUncompressedBytes ?? DefaultMaxUncompressedBytes;
        if (!options.MaxUncompressedBytes.HasValue && _diskProvider != null)
        {
            try
            {
                var freeSpace = _diskProvider.GetAvailableFreeSpace(canonicalTargetRoot);
                if (freeSpace > 0 && freeSpace < effectiveMaxBytes)
                {
                    effectiveMaxBytes = freeSpace;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not determine free disk space for {0}", canonicalTargetRoot);
            }
        }

        var maxCompressionRatio = options.MaxCompressionRatio > 0 ? options.MaxCompressionRatio : 50.0;
        var minBytesForRatioCheck = options.MinBytesForRatioCheck;

        var header = new byte[2];
        var readHeader = await archiveStream.ReadAsync(header.AsMemory(0, 2), cancellationToken);
        var isGzip = readHeader == 2 && header[0] == 0x1F && header[1] == 0x8B;

        Stream effectiveStream;
        if (archiveStream.CanSeek)
        {
            archiveStream.Position = 0;
            effectiveStream = archiveStream;
        }
        else
        {
            effectiveStream = new PrefixStream(header.AsMemory(0, readHeader), archiveStream);
        }

        var extractedFiles = new List<string>();
        long totalUncompressedBytes = 0;
        var duplicateInfoHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateTorrentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PackageManifest parsedManifest = null;

        using var countingStream = new CountingStream(effectiveStream);
        await using var decompressedStream = isGzip
            ? new GZipStream(countingStream, CompressionMode.Decompress, leaveOpen: true)
            : (Stream)countingStream;

        await using var tarReader = new TarReader(decompressedStream, leaveOpen: true);

        try
        {
            TarEntry entry;
            while ((entry = await tarReader.GetNextEntryAsync(cancellationToken: cancellationToken)) != null)
            {
                // 1. Strict Link Type Rejection
                if (entry.EntryType == TarEntryType.SymbolicLink || entry.EntryType == TarEntryType.HardLink)
                {
                    throw new SecurityException($"Symlink or hardlink rejected in package entry: {entry.Name} (Type: {entry.EntryType})");
                }

                if (entry.EntryType != TarEntryType.RegularFile &&
                    entry.EntryType != TarEntryType.V7RegularFile &&
                    entry.EntryType != TarEntryType.Directory)
                {
                    throw new SecurityException($"Disallowed tar entry type '{entry.EntryType}' in entry: {entry.Name}");
                }

                // 2. Canonical Path Sandboxing & Zip-Slip Defense
                ValidateTarEntryName(entry.Name);

                var destinationPath = Path.GetFullPath(Path.Combine(canonicalTargetRoot, entry.Name));
                if (entry.EntryType == TarEntryType.Directory && destinationPath == canonicalTargetRoot)
                {
                    continue;
                }

                if (!destinationPath.StartsWith(canonicalTargetRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new SecurityException($"Potential Zip-Slip attack detected in entry: {entry.Name}");
                }

                // 3. Deduplication check before extracting: candidate infohash checked against active library
                var isDuplicateEntry = false;
                if (options.SkipDuplicates && _torrentService != null)
                {
                    if (entry.Name.StartsWith("metainfo/", StringComparison.OrdinalIgnoreCase))
                    {
                        var hash = Path.GetFileNameWithoutExtension(entry.Name).ToLowerInvariant();
                        if (duplicateInfoHashes.Contains(hash) || _torrentService.ExistsByInfoHash(hash))
                        {
                            duplicateInfoHashes.Add(hash);
                            isDuplicateEntry = true;
                        }
                    }
                    else if (entry.Name.StartsWith("fastresume/", StringComparison.OrdinalIgnoreCase))
                    {
                        var hash = Path.GetFileNameWithoutExtension(entry.Name).ToLowerInvariant();
                        if (duplicateInfoHashes.Contains(hash) || _torrentService.ExistsByInfoHash(hash))
                        {
                            duplicateInfoHashes.Add(hash);
                            isDuplicateEntry = true;
                        }
                    }
                    else if (entry.Name.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
                    {
                        var rel = entry.Name.Substring("content/".Length).TrimStart('/', '\\');
                        var rootTorrentName = rel.Split(new[] { '/', '\\' }, 2)[0];
                        if (duplicateTorrentNames.Contains(rootTorrentName))
                        {
                            isDuplicateEntry = true;
                        }
                    }
                }

                if (isDuplicateEntry)
                {
                    _logger.Info("Skipping extraction of duplicate entry '{0}' as torrent already exists in active library.", entry.Name);
                    if (entry.DataStream != null)
                    {
                        await entry.DataStream.CopyToAsync(Stream.Null, cancellationToken);
                    }
                    continue;
                }

                // 4. Check declared entry length limit if specified
                if (entry.Length > effectiveMaxBytes)
                {
                    throw new SecurityException($"Tar entry '{entry.Name}' declared length of {entry.Length} bytes exceeds limit of {effectiveMaxBytes} bytes.");
                }

                // 5. Extract entry
                if (entry.EntryType == TarEntryType.Directory)
                {
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    var parentDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    await using (var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
                    {
                        if (entry.DataStream != null)
                        {
                            var buffer = new byte[BufferSize];
                            int chunkBytes;
                            while ((chunkBytes = await entry.DataStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                            {
                                totalUncompressedBytes += chunkBytes;

                                if (totalUncompressedBytes > effectiveMaxBytes)
                                {
                                    throw new SecurityException($"Decompression bomb rejected: total uncompressed size exceeded limit of {effectiveMaxBytes} bytes.");
                                }

                                if (totalUncompressedBytes > minBytesForRatioCheck)
                                {
                                    var compressedBytes = Math.Max(1L, countingStream.BytesRead);
                                    var ratio = (double)totalUncompressedBytes / compressedBytes;
                                    if (ratio > maxCompressionRatio)
                                    {
                                        throw new SecurityException($"Decompression bomb rejected: compression ratio {ratio:F1}:1 exceeded maximum allowed ratio of {maxCompressionRatio}:1.");
                                    }
                                }

                                await fs.WriteAsync(buffer.AsMemory(0, chunkBytes), cancellationToken);
                            }
                        }
                    }

                    extractedFiles.Add(destinationPath);

                    // If manifest was extracted, immediately inspect candidate torrents for deduplication
                    if (entry.Name == "manifest.json" && parsedManifest == null)
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(destinationPath, cancellationToken);
                            parsedManifest = json.FromJson<PackageManifest>();
                            if (parsedManifest?.Torrents != null && options.SkipDuplicates && _torrentService != null)
                            {
                                foreach (var item in parsedManifest.Torrents)
                                {
                                    if (!string.IsNullOrWhiteSpace(item.InfoHash) && _torrentService.ExistsByInfoHash(item.InfoHash))
                                    {
                                        duplicateInfoHashes.Add(item.InfoHash.ToLowerInvariant());
                                        if (!string.IsNullOrWhiteSpace(item.Name))
                                        {
                                            duplicateTorrentNames.Add(item.Name);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Could not pre-parse manifest during extraction");
                        }
                    }
                }
            }
        }
        catch
        {
            if (isTemporarySandbox && Directory.Exists(canonicalTargetRoot))
            {
                try
                {
                    Directory.Delete(canonicalTargetRoot, true);
                }
                catch
                {
                }
            }

            throw;
        }

        // 6. Manifest & Torrent Import
        var manifestPath = Path.Combine(canonicalTargetRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            if (isTemporarySandbox && Directory.Exists(canonicalTargetRoot))
            {
                try
                {
                    Directory.Delete(canonicalTargetRoot, true);
                }
                catch
                {
                }
            }

            throw new SecurityException("Package archive is missing required manifest.json.");
        }

        var manifest = parsedManifest;
        if (manifest == null)
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            try
            {
                manifest = manifestJson.FromJson<PackageManifest>();
            }
            catch (Exception ex)
            {
                if (isTemporarySandbox && Directory.Exists(canonicalTargetRoot))
                {
                    try
                    {
                        Directory.Delete(canonicalTargetRoot, true);
                    }
                    catch
                    {
                    }
                }

                throw new SecurityException("Failed to parse package manifest JSON.", ex);
            }
        }

        if (manifest == null || manifest.SchemaVersion != 1)
        {
            if (isTemporarySandbox && Directory.Exists(canonicalTargetRoot))
            {
                try
                {
                    Directory.Delete(canonicalTargetRoot, true);
                }
                catch
                {
                }
            }

            throw new SecurityException($"Unsupported or invalid package manifest schema version: {manifest?.SchemaVersion}");
        }

        var appDataFolder = _appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory;
        var seedarrTorrentsDir = Path.Combine(appDataFolder, "torrents");
        var seedarrFastResumeDir = Path.Combine(appDataFolder, "fastresume");
        Directory.CreateDirectory(seedarrTorrentsDir);
        Directory.CreateDirectory(seedarrFastResumeDir);

        var result = new PackageImportResult
        {
            TotalBytesExtracted = totalUncompressedBytes
        };
        result.ExtractedFiles.AddRange(extractedFiles);

        var metainfoDir = Path.Combine(canonicalTargetRoot, "metainfo");
        var fastresumeDirInArchive = Path.Combine(canonicalTargetRoot, "fastresume");

        if (Directory.Exists(metainfoDir))
        {
            var torrentFiles = Directory.GetFiles(metainfoDir, "*.torrent");
            foreach (var metainfoPath in torrentFiles)
            {
                var torrentFileName = Path.GetFileName(metainfoPath);
                var destTorrentFile = Path.Combine(seedarrTorrentsDir, torrentFileName);
                if (!string.Equals(Path.GetFullPath(metainfoPath), Path.GetFullPath(destTorrentFile), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(metainfoPath, destTorrentFile, overwrite: true);
                }

                var infoHash = Path.GetFileNameWithoutExtension(torrentFileName).ToLowerInvariant();
                var manifestItem = manifest.Torrents?.FirstOrDefault(t =>
                    string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase));

                if (duplicateInfoHashes.Contains(infoHash))
                {
                    continue;
                }

                Torrent importedTorrent = null;

                if (options.RestoreTorrents && _torrentImportService != null)
                {
                    try
                    {
                        await using var ts = File.OpenRead(destTorrentFile);
                        importedTorrent = _torrentImportService.ImportFromFile(ts, torrentFileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to import torrent from file {0}", destTorrentFile);
                    }
                }
                else if (options.RestoreTorrents && _torrentService != null)
                {
                    try
                    {
                        importedTorrent = _torrentService.GetByInfoHash(infoHash);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Torrent {0} not found in database.", infoHash);
                    }
                }

                if (importedTorrent != null)
                {
                    if (manifestItem != null && !string.IsNullOrWhiteSpace(manifestItem.Category) && string.IsNullOrWhiteSpace(importedTorrent.Category))
                    {
                        importedTorrent.Category = manifestItem.Category;
                        _torrentService?.Update(importedTorrent);
                    }

                    // Restore trackers
                    if (_trackerEntryService != null && manifestItem?.Trackers != null && manifestItem.Trackers.Count > 0)
                    {
                        try
                        {
                            var existingTrackers = _trackerEntryService.GetByTorrentId(importedTorrent.Id);
                            var existingUrls = new HashSet<string>(existingTrackers?.Select(t => t.Url) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                            var trackersToAdd = new List<TrackerEntry>();

                            foreach (var trk in manifestItem.Trackers)
                            {
                                if (!string.IsNullOrWhiteSpace(trk.Url) && existingUrls.Add(trk.Url))
                                {
                                    var status = Enum.TryParse<TrackerStatus>(trk.Status, true, out var parsedStatus) ? parsedStatus : TrackerStatus.Unknown;
                                    trackersToAdd.Add(new TrackerEntry
                                    {
                                        TorrentId = importedTorrent.Id,
                                        Url = trk.Url,
                                        Tier = trk.Tier,
                                        Enabled = trk.Enabled,
                                        Status = status,
                                        Seeders = trk.Seeders,
                                        Leechers = trk.Leechers,
                                        TotalAnnounces = trk.TotalAnnounces,
                                        SuccessfulAnnounces = trk.SuccessfulAnnounces,
                                        LastAnnounce = trk.LastAnnounce
                                    });
                                }
                            }

                            if (trackersToAdd.Count > 0)
                            {
                                _trackerEntryService.AddMany(trackersToAdd);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Failed to restore tracker entries for torrent {0}", importedTorrent.Id);
                        }
                    }

                    result.Torrents.Add(new PackageImportTorrentSummary
                    {
                        Id = importedTorrent.Id,
                        Name = importedTorrent.Name,
                        InfoHash = importedTorrent.InfoHash,
                        Category = importedTorrent.Category,
                        TotalSize = importedTorrent.TotalSize,
                        Tags = manifestItem?.Tags ?? new List<string>(),
                        IsDuplicate = false
                    });
                }
                else
                {
                    result.Torrents.Add(new PackageImportTorrentSummary
                    {
                        Id = manifestItem?.Id ?? 0,
                        Name = manifestItem?.Name ?? torrentFileName,
                        InfoHash = infoHash,
                        Category = manifestItem?.Category,
                        TotalSize = manifestItem?.TotalSize ?? 0,
                        Tags = manifestItem?.Tags ?? new List<string>(),
                        IsDuplicate = false
                    });
                }

                // Restore fastresume file if present
                if (Directory.Exists(fastresumeDirInArchive))
                {
                    var srcFastResume = Path.Combine(fastresumeDirInArchive, $"{infoHash}.fastresume");
                    if (File.Exists(srcFastResume))
                    {
                        var destFastResume = Path.Combine(seedarrFastResumeDir, $"{infoHash}.fastresume");
                        if (!string.Equals(Path.GetFullPath(srcFastResume), Path.GetFullPath(destFastResume), StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(srcFastResume, destFastResume, overwrite: true);
                        }

                        if (_fastResumeService != null && importedTorrent != null)
                        {
                            try
                            {
                                _fastResumeService.LoadFastResume(importedTorrent);
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug(ex, "Failed to load fastresume for {0}", importedTorrent.InfoHash);
                            }
                        }
                    }
                }
            }
        }

        // Record duplicate candidate torrents from manifest
        if (manifest.Torrents != null)
        {
            foreach (var manifestItem in manifest.Torrents)
            {
                var infoHash = manifestItem.InfoHash?.ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(infoHash) && duplicateInfoHashes.Contains(infoHash))
                {
                    if (!result.SkippedDuplicates.Contains(infoHash))
                    {
                        result.SkippedDuplicates.Add(infoHash);
                    }

                    if (!result.Torrents.Any(t => string.Equals(t.InfoHash, infoHash, StringComparison.OrdinalIgnoreCase)))
                    {
                        var existing = _torrentService?.GetByInfoHash(infoHash);
                        result.Torrents.Add(new PackageImportTorrentSummary
                        {
                            Id = existing?.Id ?? manifestItem.Id,
                            Name = existing?.Name ?? manifestItem.Name ?? infoHash,
                            InfoHash = infoHash,
                            Category = existing?.Category ?? manifestItem.Category,
                            TotalSize = existing?.TotalSize ?? manifestItem.TotalSize,
                            Tags = manifestItem.Tags ?? new List<string>(),
                            IsDuplicate = true
                        });
                    }
                }
            }
        }

        // Restore content payload if specified
        var contentDir = Path.Combine(canonicalTargetRoot, "content");
        if (!string.IsNullOrWhiteSpace(options.DestinationPath) && Directory.Exists(contentDir))
        {
            var canonicalDest = Path.GetFullPath(options.DestinationPath);
            Directory.CreateDirectory(canonicalDest);

            var files = Directory.GetFiles(contentDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(contentDir, file);
                var destFile = Path.GetFullPath(Path.Combine(canonicalDest, rel));

                if (!destFile.StartsWith(canonicalDest + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new SecurityException($"Potential Zip-Slip attack detected during payload relocation: {rel}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(file, destFile, overwrite: true);
                }
            }
        }

        if (isTemporarySandbox && Directory.Exists(canonicalTargetRoot))
        {
            try
            {
                Directory.Delete(canonicalTargetRoot, true);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to clean up temporary sandbox {0}", canonicalTargetRoot);
            }
        }

        var importedCount = result.Torrents.Count(t => !t.IsDuplicate);
        var duplicateCount = result.SkippedDuplicates.Count;

        if (importedCount == 0 && duplicateCount > 0)
        {
            result.Message = $"All {duplicateCount} candidate torrent(s) already exist in active library (skipped duplicates).";
        }
        else if (duplicateCount > 0)
        {
            result.Message = $"Successfully imported {importedCount} torrent(s), skipped {duplicateCount} duplicate(s).";
        }
        else
        {
            result.Message = $"Successfully imported {importedCount} torrent(s).";
        }

        result.Success = true;
        return result;
    }

    public static void ValidateTarEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            throw new SecurityException("Tar entry name is empty or whitespace.");
        }

        var normalized = entryName.Replace('\\', '/');

        // Check for drive letter: e.g. "C:..."
        if (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
        {
            throw new SecurityException($"Potential Zip-Slip attack detected in entry: {entryName}");
        }

        // Check for absolute or UNC paths: starts with '/' or '\\'
        if (normalized.StartsWith('/') || normalized.StartsWith('\\'))
        {
            throw new SecurityException($"Potential Zip-Slip attack detected in entry: {entryName}");
        }

        // Check for directory traversal sequences like ".."
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s == ".."))
        {
            throw new SecurityException($"Potential Zip-Slip attack detected in entry: {entryName}");
        }
    }
}

internal class CountingStream : Stream
{
    private readonly Stream _inner;
    private long _bytesRead;

    public CountingStream(Stream inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public long BytesRead => _bytesRead;

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0)
        {
            _bytesRead += read;
        }

        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        if (read > 0)
        {
            _bytesRead += read;
        }

        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        if (read > 0)
        {
            _bytesRead += read;
        }

        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        if (read > 0)
        {
            _bytesRead += read;
        }

        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
}

internal class PrefixStream : Stream
{
    private readonly ReadOnlyMemory<byte> _prefix;
    private readonly Stream _inner;
    private int _prefixPosition;

    public PrefixStream(ReadOnlyMemory<byte> prefix, Stream inner)
    {
        _prefix = prefix;
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _prefix.Length + _inner.Length;

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var readFromPrefix = 0;
        if (_prefixPosition < _prefix.Length)
        {
            readFromPrefix = Math.Min(count, _prefix.Length - _prefixPosition);
            _prefix.Span.Slice(_prefixPosition, readFromPrefix).CopyTo(buffer.AsSpan(offset, readFromPrefix));
            _prefixPosition += readFromPrefix;
            offset += readFromPrefix;
            count -= readFromPrefix;
        }

        if (count > 0)
        {
            return readFromPrefix + _inner.Read(buffer, offset, count);
        }

        return readFromPrefix;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var readFromPrefix = 0;
        if (_prefixPosition < _prefix.Length)
        {
            readFromPrefix = Math.Min(count, _prefix.Length - _prefixPosition);
            _prefix.Slice(_prefixPosition, readFromPrefix).CopyTo(buffer.AsMemory(offset, readFromPrefix));
            _prefixPosition += readFromPrefix;
            offset += readFromPrefix;
            count -= readFromPrefix;
        }

        if (count > 0)
        {
            var innerRead = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            return readFromPrefix + innerRead;
        }

        return readFromPrefix;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var readFromPrefix = 0;
        if (_prefixPosition < _prefix.Length)
        {
            readFromPrefix = Math.Min(buffer.Length, _prefix.Length - _prefixPosition);
            _prefix.Slice(_prefixPosition, readFromPrefix).CopyTo(buffer);
            _prefixPosition += readFromPrefix;
            buffer = buffer.Slice(readFromPrefix);
        }

        if (buffer.Length > 0)
        {
            var innerRead = await _inner.ReadAsync(buffer, cancellationToken);
            return readFromPrefix + innerRead;
        }

        return readFromPrefix;
    }
}
