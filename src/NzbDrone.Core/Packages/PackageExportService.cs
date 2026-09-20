using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Packages;

public class PackageExportService : IPackageExportService
{
    public const int BoundedBufferSize = 65536; // 64 KiB

    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IFastResumeService _fastResumeService;
    private readonly IFastResumeBencodeSerializer _bencodeSerializer;
    private readonly ITagService _tagService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly ISyntheticMetadataGenerator _syntheticMetadataGenerator;
    private readonly Logger _logger;

    public PackageExportService(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService = null,
        IFastResumeService fastResumeService = null,
        IFastResumeBencodeSerializer bencodeSerializer = null,
        ITagService tagService = null,
        IAppFolderInfo appFolderInfo = null,
        ISyntheticMetadataGenerator syntheticMetadataGenerator = null)
    {
        _torrentService = torrentService ?? throw new ArgumentNullException(nameof(torrentService));
        _torrentFileService = torrentFileService;
        _fastResumeService = fastResumeService;
        _bencodeSerializer = bencodeSerializer ?? new FastResumeBencodeSerializer();
        _tagService = tagService;
        _appFolderInfo = appFolderInfo;
        _syntheticMetadataGenerator = syntheticMetadataGenerator ?? new SyntheticMetadataGenerator();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task ExportPackageAsync(
        Stream outputStream,
        IEnumerable<int> torrentIds,
        bool includePayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(torrentIds);

        var idList = torrentIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            throw new ArgumentException("At least one torrent ID must be provided.", nameof(torrentIds));
        }

        var torrents = new List<Torrent>();
        foreach (var id in idList)
        {
            Torrent torrent = null;
            try
            {
                torrent = _torrentService.Get(id);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error retrieving torrent with ID {0} for export.", id);
            }

            if (torrent != null)
            {
                torrents.Add(torrent);
            }
            else
            {
                _logger.Warn("Torrent with ID {0} not found for export.", id);
            }
        }

        if (torrents.Count == 0)
        {
            throw new ArgumentException("None of the specified torrents were found.", nameof(torrentIds));
        }

        await using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen: true))
        {
            await using (var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true))
            {
                // 1. Write manifest.json
                await WriteManifestEntryAsync(tarWriter, torrents, cancellationToken);

                // 2. Export each torrent
                foreach (var torrent in torrents)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await WriteMetainfoEntryAsync(tarWriter, torrent, cancellationToken);
                    await WriteFastResumeEntryAsync(tarWriter, torrent, cancellationToken);

                    if (includePayload)
                    {
                        await WritePayloadEntriesAsync(tarWriter, torrent, cancellationToken);
                    }
                }
            }
        }
    }

    private async Task WriteManifestEntryAsync(TarWriter tarWriter, List<Torrent> torrents, CancellationToken cancellationToken)
    {
        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            ExportedAt = DateTime.UtcNow,
            Client = "Seedarr",
            Torrents = torrents.Select(t => new PackageTorrentItem
            {
                Id = t.Id,
                Name = t.Name,
                InfoHash = t.InfoHash,
                Category = t.Category,
                Tags = ResolveTags(t),
                TotalSize = t.TotalSize,
                PieceCount = t.PieceCount,
                PieceLength = t.PieceLength,
                IsPrivate = t.IsPrivate
            }).ToList()
        };

        var json = manifest.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream(bytes);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
        {
            DataStream = ms
        };

        await tarWriter.WriteEntryAsync(entry, cancellationToken);
    }

    private List<string> ResolveTags(Torrent torrent)
    {
        if (torrent.TagIds == null || torrent.TagIds.Count == 0)
        {
            return new List<string>();
        }

        if (_tagService != null)
        {
            try
            {
                var labels = _tagService.GetLabelsForTagIds(torrent.TagIds);
                if (labels != null && labels.Count > 0)
                {
                    return labels;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to resolve tag labels for torrent {0}", torrent.Id);
            }
        }

        return torrent.TagIds.Select(t => t.ToString()).ToList();
    }

    private async Task WriteMetainfoEntryAsync(TarWriter tarWriter, Torrent torrent, CancellationToken cancellationToken)
    {
        var infoHash = !string.IsNullOrWhiteSpace(torrent.InfoHash) ? torrent.InfoHash.ToLowerInvariant() : "unknown";
        var entryName = $"metainfo/{infoHash}.torrent";

        // Check if SourcePath exists on disk
        if (!string.IsNullOrWhiteSpace(torrent.SourcePath) && File.Exists(torrent.SourcePath))
        {
            try
            {
                await StreamFileToTarAsync(tarWriter, torrent.SourcePath, entryName, cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to read metainfo from SourcePath {0}", torrent.SourcePath);
            }
        }

        // Check app data folder
        var appDataFolder = _appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory;
        var candidate = Path.Combine(appDataFolder, "torrents", $"{infoHash}.torrent");
        if (File.Exists(candidate))
        {
            try
            {
                await StreamFileToTarAsync(tarWriter, candidate, entryName, cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to read metainfo from app data path {0}", candidate);
            }
        }

        // Fallback: Generate bencoded torrent metainfo
        var torrentBytes = GenerateTorrentBytes(torrent);
        using var ms = new MemoryStream(torrentBytes);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = ms
        };
        await tarWriter.WriteEntryAsync(entry, cancellationToken);
    }

    private byte[] GenerateTorrentBytes(Torrent torrent)
    {
        var dict = new BDictionary();

        if (_syntheticMetadataGenerator != null)
        {
            try
            {
                var infoBytes = _syntheticMetadataGenerator.GenerateMetadataBytes(torrent);
                if (infoBytes != null && infoBytes.Length > 0)
                {
                    var parser = new BencodeParser();
                    dict["info"] = parser.Parse<BDictionary>(infoBytes);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Synthetic metadata generator failed for {0}", torrent.InfoHash);
            }
        }

        if (!dict.ContainsKey("info"))
        {
            var infoDict = new BDictionary
            {
                ["name"] = new BString(torrent.Name ?? "torrent"),
                ["piece length"] = new BNumber(torrent.PieceLength > 0 ? torrent.PieceLength : 262144),
                ["length"] = new BNumber(torrent.TotalSize),
                ["pieces"] = new BString(Array.Empty<byte>())
            };
            dict["info"] = infoDict;
        }

        if (!string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            dict["announce"] = new BString(torrent.TrackerUrl);
        }

        if (!string.IsNullOrWhiteSpace(torrent.Comment))
        {
            dict["comment"] = new BString(torrent.Comment);
        }

        if (!string.IsNullOrWhiteSpace(torrent.CreatedBy))
        {
            dict["created by"] = new BString(torrent.CreatedBy);
        }

        return dict.EncodeAsBytes();
    }

    private async Task WriteFastResumeEntryAsync(TarWriter tarWriter, Torrent torrent, CancellationToken cancellationToken)
    {
        var infoHash = !string.IsNullOrWhiteSpace(torrent.InfoHash) ? torrent.InfoHash.ToLowerInvariant() : "unknown";
        var entryName = $"fastresume/{infoHash}.fastresume";

        FastResumeData data = null;
        if (_fastResumeService != null)
        {
            try
            {
                data = _fastResumeService.LoadFastResume(torrent) ?? _fastResumeService.LoadFastResume(torrent.InfoHash);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to load FastResume via IFastResumeService for {0}", torrent.InfoHash);
            }
        }

        if (data == null)
        {
            var appDataFolder = _appFolderInfo?.AppDataFolder ?? AppContext.BaseDirectory;
            var resumePath = Path.Combine(appDataFolder, "fastresume", $"{infoHash}.fastresume");
            if (File.Exists(resumePath))
            {
                try
                {
                    var fileBytes = await File.ReadAllBytesAsync(resumePath, cancellationToken);
                    if (_bencodeSerializer.IsBencode(fileBytes))
                    {
                        data = _bencodeSerializer.Deserialize(fileBytes);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to read FastResume file {0}", resumePath);
                }
            }
        }

        if (data == null)
        {
            data = new FastResumeData
            {
                InfoHash = torrent.InfoHash,
                SavePath = torrent.SavePath,
                Progress = torrent.Progress,
                Uploaded = torrent.Uploaded,
                Downloaded = torrent.Downloaded,
                Status = torrent.Status.ToString(),
                SequentialDownload = torrent.SequentialDownload,
                SeedingTime = torrent.SeedingTime,
                SavedAt = DateTime.UtcNow,
                Allocation = "sparse",
                Files = new List<FastResumeFileEntry>()
            };

            if (torrent.PieceCount > 0 && torrent.Progress >= 1.0)
            {
                var bitfield = new bool[torrent.PieceCount];
                Array.Fill(bitfield, true);
                data.Bitfield = bitfield;
                data.FinishedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
        }

        var resumeBytes = _bencodeSerializer.Serialize(data);
        using var ms = new MemoryStream(resumeBytes);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = ms
        };

        await tarWriter.WriteEntryAsync(entry, cancellationToken);
    }

    private async Task WritePayloadEntriesAsync(TarWriter tarWriter, Torrent torrent, CancellationToken cancellationToken)
    {
        List<TorrentFile> torrentFiles = null;
        if (_torrentFileService != null && torrent.Id > 0)
        {
            try
            {
                torrentFiles = _torrentFileService.GetByTorrentId(torrent.Id);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to retrieve torrent files for torrent {0}", torrent.Id);
            }
        }

        var basePath = !string.IsNullOrWhiteSpace(torrent.SavePath) ? torrent.SavePath : torrent.SourcePath;

        if (torrentFiles != null && torrentFiles.Count > 0)
        {
            foreach (var tf in torrentFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (tf.IsPaddingFile)
                {
                    continue;
                }

                var diskPath = ResolveFileDiskPath(basePath, torrent.Name, tf.Path);
                if (!File.Exists(diskPath))
                {
                    _logger.Warn("Payload file {0} does not exist on disk for torrent {1}. Skipping.", diskPath, torrent.Name);
                    continue;
                }

                var rel = (tf.Path ?? string.Empty).Replace('\\', '/').TrimStart('/');
                var entryPath = rel.StartsWith(torrent.Name + "/", StringComparison.OrdinalIgnoreCase)
                    ? $"content/{rel}"
                    : $"content/{torrent.Name}/{rel}";

                await StreamFileToTarAsync(tarWriter, diskPath, entryPath, cancellationToken);
            }
        }
        else if (!string.IsNullOrWhiteSpace(basePath))
        {
            if (File.Exists(basePath))
            {
                var entryPath = $"content/{torrent.Name}";
                await StreamFileToTarAsync(tarWriter, basePath, entryPath, cancellationToken);
            }
            else if (Directory.Exists(basePath))
            {
                var rootDir = Directory.Exists(Path.Combine(basePath, torrent.Name))
                    ? Path.Combine(basePath, torrent.Name)
                    : basePath;

                var files = Directory.GetFiles(rootDir, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rel = Path.GetRelativePath(rootDir, file).Replace('\\', '/').TrimStart('/');
                    var entryPath = $"content/{torrent.Name}/{rel}";
                    await StreamFileToTarAsync(tarWriter, file, entryPath, cancellationToken);
                }
            }
            else
            {
                _logger.Warn("Base path {0} does not exist for torrent {1}. Skipping payload.", basePath, torrent.Name);
            }
        }
    }

    private async Task StreamFileToTarAsync(TarWriter tarWriter, string diskPath, string entryPath, CancellationToken cancellationToken)
    {
        try
        {
            var fileOptions = new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                BufferSize = BoundedBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };

            await using var fs = new FileStream(diskPath, fileOptions);
            using var boundedStream = new BoundedReadStream(fs, BoundedBufferSize);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryPath)
            {
                DataStream = boundedStream
            };

            await tarWriter.WriteEntryAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to stream file {0} into archive under {1}", diskPath, entryPath);
        }
    }

    public static string ResolveFileDiskPath(string basePath, string torrentName, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return basePath;
        }

        if (string.IsNullOrWhiteSpace(basePath))
        {
            return Path.GetFullPath(filePath);
        }

        var directCombined = Path.Combine(basePath, filePath);
        if (File.Exists(directCombined))
        {
            return directCombined;
        }

        if (!string.IsNullOrWhiteSpace(torrentName))
        {
            var torrentSubPath = Path.Combine(basePath, torrentName, filePath);
            if (File.Exists(torrentSubPath))
            {
                return torrentSubPath;
            }
        }

        return directCombined;
    }
}

public class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly int _maxReadSize;

    public BoundedReadStream(Stream inner, int maxReadSize = 65536)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _maxReadSize = maxReadSize > 0 ? maxReadSize : 65536;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var toRead = Math.Min(count, _maxReadSize);
        return _inner.Read(buffer, offset, toRead);
    }

    public override int Read(Span<byte> buffer)
    {
        var slice = buffer.Length > _maxReadSize ? buffer[.._maxReadSize] : buffer;
        return _inner.Read(slice);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var toRead = Math.Min(count, _maxReadSize);
        return _inner.ReadAsync(buffer, offset, toRead, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var slice = buffer.Length > _maxReadSize ? buffer[.._maxReadSize] : buffer;
        return _inner.ReadAsync(slice, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
