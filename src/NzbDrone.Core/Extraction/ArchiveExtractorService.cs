using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace NzbDrone.Core.Extraction;

public class ArchiveExtractorService : IArchiveExtractorService
{
    public const double DefaultFreeSpaceSafetyMargin = 1.15;
    public const double DefaultMaxCompressionRatio = 100.0;
    public const long DefaultMaxSingleFileUncompressedSize = 250L * 1024 * 1024 * 1024; // 250 GB

    private static readonly Regex SecondaryPartRarRegex = new(@"\.part(?!0*1\.rar$)(\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PrimaryPartRarRegex = new(@"\.part0*1\.rar$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecondaryVolumeRegex = new(@"\.[r-z]\d{2,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecondarySplitArchiveRegex = new(@"\.(?:7z|zip)\.(?!0*1$)\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PrimarySplitArchiveRegex = new(@"\.(?:7z|zip)\.0*1$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IEventAggregator _eventAggregator;
    private readonly IDiskProvider _diskProvider;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public ArchiveExtractorService(IEventAggregator eventAggregator, IDiskProvider diskProvider = null)
    {
        _eventAggregator = eventAggregator;
        _diskProvider = diskProvider ?? new DiskProvider();
    }

    public double FreeSpaceSafetyMargin { get; set; } = DefaultFreeSpaceSafetyMargin;

    public double MaxCompressionRatio { get; set; } = DefaultMaxCompressionRatio;

    public long MaxSingleFileUncompressedSize { get; set; } = DefaultMaxSingleFileUncompressedSize;

    public bool IsPrimaryArchive(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        // Secondary multi-part RAR slices: .part02.rar, .part03.rar, etc.
        if (SecondaryPartRarRegex.IsMatch(fileName))
        {
            return false;
        }

        // Primary multi-part RAR: .part01.rar, .part1.rar, .part001.rar
        if (PrimaryPartRarRegex.IsMatch(fileName))
        {
            return true;
        }

        // Secondary old-style slices: .r00, .r01, .s00, .t00, etc. (also .z01, .z02)
        if (SecondaryVolumeRegex.IsMatch(fileName))
        {
            return false;
        }

        // Split 7z or zip slices: .7z.002, .zip.002, etc. (where part > 1)
        if (SecondarySplitArchiveRegex.IsMatch(fileName))
        {
            return false;
        }

        // Split 7z or zip header: .7z.001, .zip.001
        if (PrimarySplitArchiveRegex.IsMatch(fileName))
        {
            return true;
        }

        // Standard .zip
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Standard .7z
        if (fileName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Standard .rar (when not .partXX.rar)
        if (fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                var baseName = Path.GetFileNameWithoutExtension(filePath);
                try
                {
                    var companionPart1 = Directory.EnumerateFiles(dir, $"{baseName}.part*.rar")
                        .Any(f => PrimaryPartRarRegex.IsMatch(Path.GetFileName(f)));

                    if (companionPart1)
                    {
                        return false;
                    }
                }
                catch
                {
                    // Fall back to true if enumeration fails
                }
            }

            return true;
        }

        // Optional .tar.gz / .tgz
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public Task<ArchiveExtractionResult> ExtractTorrentArchiveAsync(Torrent torrent, string destination = null, bool deleteArchive = false)
    {
        if (torrent == null)
        {
            const string nullError = "Torrent cannot be null";
            _logger.Error(nullError);
            return Task.FromResult(new ArchiveExtractionResult
            {
                Success = false,
                ErrorMessage = nullError,
            });
        }

        var rootDir = GetTorrentDirectory(torrent);
        if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
        {
            var missingError = $"Torrent directory not found for '{torrent.Name}' (SavePath: {torrent.SavePath}, SourcePath: {torrent.SourcePath})";
            _logger.Warn(missingError);
            _eventAggregator.PublishEvent(new ArchiveExtractionFailedEvent(torrent, missingError));
            return Task.FromResult(new ArchiveExtractionResult
            {
                Success = false,
                ErrorMessage = missingError,
            });
        }

        var destDir = string.IsNullOrWhiteSpace(destination) ? rootDir : destination;
        if (!Directory.Exists(destDir))
        {
            try
            {
                Directory.CreateDirectory(destDir);
            }
            catch (Exception ex)
            {
                var createError = $"Failed to create destination directory '{destDir}': {ex.Message}";
                _logger.Error(ex, createError);
                _eventAggregator.PublishEvent(new ArchiveExtractionFailedEvent(torrent, createError));
                return Task.FromResult(new ArchiveExtractionResult
                {
                    Success = false,
                    ErrorMessage = createError,
                    DestinationPath = destDir,
                });
            }
        }

        List<string> primaryArchives;
        try
        {
            primaryArchives = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
                .Where(IsPrimaryArchive)
                .OrderBy(f => f)
                .ToList();
        }
        catch (Exception ex)
        {
            var scanError = $"Failed to scan directory '{rootDir}' for archives: {ex.Message}";
            _logger.Error(ex, scanError);
            _eventAggregator.PublishEvent(new ArchiveExtractionFailedEvent(torrent, scanError));
            return Task.FromResult(new ArchiveExtractionResult
            {
                Success = false,
                ErrorMessage = scanError,
                DestinationPath = destDir,
            });
        }

        if (primaryArchives.Count == 0)
        {
            _logger.Info("No primary archives found in '{0}' for torrent '{1}'", rootDir, torrent.Name);
            return Task.FromResult(new ArchiveExtractionResult
            {
                Success = true,
                ExtractedFiles = new List<string>(),
                DestinationPath = destDir,
            });
        }

        var result = new ArchiveExtractionResult
        {
            DestinationPath = destDir,
        };

        var extractedFiles = new List<string>();
        var archivesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var canonicalTargetDir = Path.GetFullPath(destDir);
            if (!canonicalTargetDir.EndsWith(Path.DirectorySeparatorChar))
            {
                canonicalTargetDir += Path.DirectorySeparatorChar;
            }

            // Pre-extraction validation across all primary archives (Zip-Slip, Zip-Bomb, Symlinks)
            long totalUncompressedSize = 0;
            foreach (var archivePath in primaryArchives)
            {
                ValidateArchive(archivePath, canonicalTargetDir, out var archiveSize);
                totalUncompressedSize += archiveSize;
            }

            // Pre-extraction disk space verification with safety margin
            var availableSpace = _diskProvider.GetAvailableFreeSpace(canonicalTargetDir);
            var requiredSpace = (long)Math.Ceiling(totalUncompressedSize * FreeSpaceSafetyMargin);
            if (availableSpace < requiredSpace)
            {
                var spaceError = $"Insufficient free disk space on '{destDir}'. Required: {requiredSpace:N0} bytes (including {(FreeSpaceSafetyMargin - 1.0) * 100:0}% safety margin for {totalUncompressedSize:N0} bytes uncompressed), Available: {availableSpace:N0} bytes.";
                _logger.Error(spaceError);
                _eventAggregator.PublishEvent(new ArchiveExtractionFailedEvent(torrent, spaceError));
                return Task.FromResult(new ArchiveExtractionResult
                {
                    Success = false,
                    ErrorMessage = spaceError,
                    DestinationPath = destDir,
                });
            }

            foreach (var archivePath in primaryArchives)
            {
                _logger.Info("Extracting archive '{0}' to '{1}'", archivePath, destDir);
                var extracted = ExtractArchiveFile(archivePath, destDir);
                extractedFiles.AddRange(extracted);

                if (deleteArchive)
                {
                    archivesToDelete.Add(archivePath);
                    CollectCompanionVolumes(archivePath, archivesToDelete);
                }
            }

            if (deleteArchive)
            {
                foreach (var archiveFile in archivesToDelete)
                {
                    try
                    {
                        if (File.Exists(archiveFile))
                        {
                            File.Delete(archiveFile);
                            _logger.Debug("Deleted archive slice '{0}'", archiveFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to delete archive slice '{0}'", archiveFile);
                    }
                }
            }

            result.Success = true;
            result.ExtractedFiles = extractedFiles;

            _logger.Info("Successfully extracted {0} file(s) for torrent '{1}'", extractedFiles.Count, torrent.Name);
            _eventAggregator.PublishEvent(new ArchiveExtractionCompletedEvent(torrent));
        }
        catch (Exception ex)
        {
            var extractError = $"Archive extraction failed for torrent '{torrent.Name}': {ex.Message}";
            _logger.Error(ex, extractError);
            result.Success = false;
            result.ErrorMessage = extractError;
            _eventAggregator.PublishEvent(new ArchiveExtractionFailedEvent(torrent, extractError));
        }

        return Task.FromResult(result);
    }

    public static string GetTorrentDirectory(Torrent torrent)
    {
        if (torrent == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(torrent.SavePath))
        {
            var torrentFolder = Path.Combine(torrent.SavePath, torrent.Name ?? string.Empty);
            if (Directory.Exists(torrentFolder))
            {
                return torrentFolder;
            }

            if (Directory.Exists(torrent.SavePath))
            {
                return torrent.SavePath;
            }

            if (File.Exists(torrent.SavePath))
            {
                return Path.GetDirectoryName(torrent.SavePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(torrent.SourcePath))
        {
            if (Directory.Exists(torrent.SourcePath))
            {
                return torrent.SourcePath;
            }

            if (File.Exists(torrent.SourcePath))
            {
                return Path.GetDirectoryName(torrent.SourcePath);
            }
        }

        return null;
    }

    public int CleanupExtractedFiles(Torrent torrent, string destination = null)
    {
        if (torrent == null)
        {
            return 0;
        }

        var rootDir = GetTorrentDirectory(torrent);
        if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
        {
            return 0;
        }

        var destDir = string.IsNullOrWhiteSpace(destination) ? rootDir : destination;
        if (!Directory.Exists(destDir))
        {
            return 0;
        }

        List<string> primaryArchives;
        try
        {
            primaryArchives = Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories)
                .Where(IsPrimaryArchive)
                .OrderBy(f => f)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to enumerate archives for post-import cleanup in '{0}'", rootDir);
            return 0;
        }

        if (primaryArchives.Count == 0)
        {
            return 0;
        }

        var canonicalDestDir = Path.GetFullPath(destDir);
        if (!canonicalDestDir.EndsWith(Path.DirectorySeparatorChar))
        {
            canonicalDestDir += Path.DirectorySeparatorChar;
        }

        var extractedCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var archivePath in primaryArchives)
        {
            try
            {
                var fileName = Path.GetFileName(archivePath);
                if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zipArchive = ZipFile.OpenRead(archivePath);
                    foreach (var entry in zipArchive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            continue;
                        }

                        var candidate = Path.GetFullPath(Path.Combine(canonicalDestDir, entry.FullName));
                        if (candidate.StartsWith(canonicalDestDir, StringComparison.OrdinalIgnoreCase))
                        {
                            extractedCandidates.Add(candidate);
                        }
                    }
                }
                else
                {
                    using var archive = ArchiveFactory.OpenArchive(archivePath);
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        var entryKey = entry.Key ?? Path.GetFileName(archivePath);
                        var candidate = Path.GetFullPath(Path.Combine(canonicalDestDir, entryKey));
                        if (candidate.StartsWith(canonicalDestDir, StringComparison.OrdinalIgnoreCase))
                        {
                            extractedCandidates.Add(candidate);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to inspect archive '{0}' during post-import cleanup", archivePath);
            }
        }

        var prunedCount = 0;
        foreach (var candidateFile in extractedCandidates)
        {
            try
            {
                if (!File.Exists(candidateFile))
                {
                    continue;
                }

                // Strictly preserve all archive slices, companion volumes, and verification files
                if (IsArchiveFile(candidateFile))
                {
                    continue;
                }

                File.Delete(candidateFile);
                prunedCount++;
                _logger.Info("Post-import cleanup: pruned extracted file '{0}' for torrent '{1}'", candidateFile, torrent.Name);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to delete extracted duplicate file '{0}' during post-import cleanup", candidateFile);
            }
        }

        return prunedCount;
    }

    public static bool IsArchiveFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        if (SecondaryPartRarRegex.IsMatch(fileName) ||
            PrimaryPartRarRegex.IsMatch(fileName) ||
            SecondaryVolumeRegex.IsMatch(fileName) ||
            SecondarySplitArchiveRegex.IsMatch(fileName) ||
            PrimarySplitArchiveRegex.IsMatch(fileName))
        {
            return true;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".rar" or ".zip" or ".7z" or ".tar" or ".gz" or ".tgz" or ".bz2" or ".sfv" or ".par2" or ".nfo" or ".torrent";
    }

    internal void ValidateArchive(string archivePath, string destination, out long totalUncompressedSize)
    {
        var canonicalTargetDir = Path.GetFullPath(destination);
        if (!canonicalTargetDir.EndsWith(Path.DirectorySeparatorChar))
        {
            canonicalTargetDir += Path.DirectorySeparatorChar;
        }

        totalUncompressedSize = 0;
        var fileName = Path.GetFileName(archivePath);

        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zipArchive = ZipFile.OpenRead(archivePath);
            foreach (var entry in zipArchive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var fullDestinationPath = Path.GetFullPath(Path.Combine(canonicalTargetDir, entry.FullName));
                if (!fullDestinationPath.StartsWith(canonicalTargetDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Zip-Slip path traversal detected: {entry.FullName}");
                }

                const int unixSymlinkMode = 0xA000;
                if (((entry.ExternalAttributes >> 16) & 0xF000) == unixSymlinkMode)
                {
                    using var reader = new StreamReader(entry.Open());
                    var linkTarget = reader.ReadToEnd().Trim();
                    if (!string.IsNullOrEmpty(linkTarget))
                    {
                        var resolvedLink = Path.IsPathRooted(linkTarget)
                            ? Path.GetFullPath(linkTarget)
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullDestinationPath) ?? canonicalTargetDir, linkTarget));

                        if (!resolvedLink.StartsWith(canonicalTargetDir, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new SecurityException($"Zip-Slip path traversal detected: {entry.FullName} -> {linkTarget}");
                        }
                    }
                }

                var uncompressedSize = entry.Length;
                var compressedSize = entry.CompressedLength;

                if (uncompressedSize > MaxSingleFileUncompressedSize)
                {
                    throw new InvalidOperationException($"Archive entry '{entry.FullName}' uncompressed size ({uncompressedSize} bytes) exceeds maximum single file limit of {MaxSingleFileUncompressedSize} bytes.");
                }

                if (uncompressedSize > 10 * 1024)
                {
                    var ratio = compressedSize > 0 ? (double)uncompressedSize / compressedSize : double.PositiveInfinity;
                    if (ratio > MaxCompressionRatio)
                    {
                        throw new InvalidOperationException($"Zip-bomb detected: archive entry '{entry.FullName}' has compression ratio of {ratio:F1}:1 (uncompressed: {uncompressedSize}, compressed: {compressedSize}), exceeding limit of {MaxCompressionRatio:F1}:1.");
                    }
                }

                totalUncompressedSize += uncompressedSize;
            }

            return;
        }

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            var entryKey = entry.Key ?? Path.GetFileName(archivePath);
            var fullDestinationPath = Path.GetFullPath(Path.Combine(canonicalTargetDir, entryKey));
            if (!fullDestinationPath.StartsWith(canonicalTargetDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Zip-Slip path traversal detected: {entryKey}");
            }

            if (!string.IsNullOrEmpty(entry.LinkTarget))
            {
                var resolvedLink = Path.IsPathRooted(entry.LinkTarget)
                    ? Path.GetFullPath(entry.LinkTarget)
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullDestinationPath) ?? canonicalTargetDir, entry.LinkTarget));

                if (!resolvedLink.StartsWith(canonicalTargetDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Zip-Slip path traversal detected: {entryKey} -> {entry.LinkTarget}");
                }
            }

            var uncompressedSize = entry.Size;
            var compressedSize = entry.CompressedSize;

            if (uncompressedSize > MaxSingleFileUncompressedSize)
            {
                throw new InvalidOperationException($"Archive entry '{entryKey}' uncompressed size ({uncompressedSize} bytes) exceeds maximum single file limit of {MaxSingleFileUncompressedSize} bytes.");
            }

            if (uncompressedSize > 10 * 1024)
            {
                var ratio = compressedSize > 0 ? (double)uncompressedSize / compressedSize : double.PositiveInfinity;
                if (ratio > MaxCompressionRatio)
                {
                    throw new InvalidOperationException($"Zip-bomb detected: archive entry '{entryKey}' has compression ratio of {ratio:F1}:1 (uncompressed: {uncompressedSize}, compressed: {compressedSize}), exceeding limit of {MaxCompressionRatio:F1}:1.");
                }
            }

            totalUncompressedSize += uncompressedSize;
        }
    }

    internal List<string> ExtractArchiveFile(string archivePath, string destination)
    {
        var extractedFiles = new List<string>();
        var fileName = Path.GetFileName(archivePath);

        var canonicalTargetDir = Path.GetFullPath(destination);
        if (!canonicalTargetDir.EndsWith(Path.DirectorySeparatorChar))
        {
            canonicalTargetDir += Path.DirectorySeparatorChar;
        }

        ValidateArchive(archivePath, canonicalTargetDir, out var totalUncompressedSize);

        var availableSpace = _diskProvider.GetAvailableFreeSpace(canonicalTargetDir);
        var requiredSpace = (long)Math.Ceiling(totalUncompressedSize * FreeSpaceSafetyMargin);
        if (availableSpace < requiredSpace)
        {
            throw new InvalidOperationException($"Insufficient free disk space on '{canonicalTargetDir}'. Required: {requiredSpace:N0} bytes (including {(FreeSpaceSafetyMargin - 1.0) * 100:0}% safety margin for {totalUncompressedSize:N0} bytes uncompressed), Available: {availableSpace:N0} bytes.");
        }

        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zipArchive = ZipFile.OpenRead(archivePath);
            foreach (var entry in zipArchive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var fullDestinationPath = Path.GetFullPath(Path.Combine(canonicalTargetDir, entry.FullName));
                if (!fullDestinationPath.StartsWith(canonicalTargetDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Zip-Slip path traversal detected: {entry.FullName}");
                }

                var parentDir = Path.GetDirectoryName(fullDestinationPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                entry.ExtractToFile(fullDestinationPath, overwrite: true);

                var fileInfo = new FileInfo(fullDestinationPath);
                if (fileInfo.LinkTarget != null)
                {
                    var resolvedLink = Path.IsPathRooted(fileInfo.LinkTarget)
                        ? Path.GetFullPath(fileInfo.LinkTarget)
                        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullDestinationPath) ?? canonicalTargetDir, fileInfo.LinkTarget));

                    if (!resolvedLink.StartsWith(canonicalTargetDir, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(fullDestinationPath);
                        }
                        catch
                        {
                            // Ignore deletion failure
                        }

                        throw new SecurityException($"Zip-Slip path traversal detected: {entry.FullName} -> {fileInfo.LinkTarget}");
                    }
                }

                extractedFiles.Add(fullDestinationPath);
            }

            return extractedFiles;
        }

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            var entryKey = entry.Key;
            if (string.IsNullOrEmpty(entryKey))
            {
                entryKey = Path.GetFileName(archivePath);
            }

            var fullDestinationPath = Path.GetFullPath(Path.Combine(canonicalTargetDir, entryKey));
            if (!fullDestinationPath.StartsWith(canonicalTargetDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Zip-Slip path traversal detected: {entryKey}");
            }

            var parentDir = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            entry.WriteToFile(fullDestinationPath, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true,
            });

            var fileInfo = new FileInfo(fullDestinationPath);
            if (fileInfo.LinkTarget != null)
            {
                var resolvedLink = Path.IsPathRooted(fileInfo.LinkTarget)
                    ? Path.GetFullPath(fileInfo.LinkTarget)
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullDestinationPath) ?? canonicalTargetDir, fileInfo.LinkTarget));

                if (!resolvedLink.StartsWith(canonicalTargetDir, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(fullDestinationPath);
                    }
                    catch
                    {
                        // Ignore deletion failure
                    }

                    throw new SecurityException($"Zip-Slip path traversal detected: {entryKey} -> {fileInfo.LinkTarget}");
                }
            }

            extractedFiles.Add(fullDestinationPath);
        }

        return extractedFiles;
    }

    private static void CollectCompanionVolumes(string primaryArchivePath, HashSet<string> filesToDelete)
    {
        var dir = Path.GetDirectoryName(primaryArchivePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            return;
        }

        var fileName = Path.GetFileName(primaryArchivePath);

        if (PrimaryPartRarRegex.IsMatch(fileName))
        {
            var match = Regex.Match(fileName, @"^(.*)\.part0*1\.rar$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var baseName = match.Groups[1].Value;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, $"{baseName}.part*.rar"))
                    {
                        filesToDelete.Add(file);
                    }
                }
                catch
                {
                    // Ignore enumeration errors
                }
            }
        }
        else if (fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, $"{baseName}.*"))
                {
                    var fName = Path.GetFileName(file);
                    if (SecondaryVolumeRegex.IsMatch(fName) || fName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                    {
                        filesToDelete.Add(file);
                    }
                }
            }
            catch
            {
                // Ignore enumeration errors
            }
        }
        else if (PrimarySplitArchiveRegex.IsMatch(fileName))
        {
            var extMatch = Regex.Match(fileName, @"^(.*)\.(7z|zip)\.0*1$", RegexOptions.IgnoreCase);
            if (extMatch.Success)
            {
                var baseName = extMatch.Groups[1].Value;
                var ext = extMatch.Groups[2].Value;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, $"{baseName}.{ext}.*"))
                    {
                        filesToDelete.Add(file);
                    }
                }
                catch
                {
                    // Ignore enumeration errors
                }
            }
        }
    }
}
