using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace NzbDrone.Core.Extraction;

public class ArchiveExtractorService : IArchiveExtractorService
{
    private static readonly Regex SecondaryPartRarRegex = new(@"\.part(?!0*1\.rar$)(\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PrimaryPartRarRegex = new(@"\.part0*1\.rar$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecondaryVolumeRegex = new(@"\.[r-z]\d{2,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecondarySplitArchiveRegex = new(@"\.(?:7z|zip)\.(?!0*1$)\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PrimarySplitArchiveRegex = new(@"\.(?:7z|zip)\.0*1$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public ArchiveExtractorService(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
    }

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

    private static List<string> ExtractArchiveFile(string archivePath, string destination)
    {
        var extractedFiles = new List<string>();
        var fileName = Path.GetFileName(archivePath);

        var fullDestination = Path.GetFullPath(destination);
        if (!fullDestination.EndsWith(Path.DirectorySeparatorChar))
        {
            fullDestination += Path.DirectorySeparatorChar;
        }

        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zipArchive = System.IO.Compression.ZipFile.OpenRead(archivePath);
            foreach (var entry in zipArchive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var outPath = Path.GetFullPath(Path.Combine(fullDestination, entry.FullName));
                if (!outPath.StartsWith(fullDestination, StringComparison.Ordinal))
                {
                    throw new IOException($"Entry '{entry.FullName}' traverses outside destination directory.");
                }

                var parentDir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                entry.ExtractToFile(outPath, overwrite: true);
                extractedFiles.Add(outPath);
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

            var outPath = Path.GetFullPath(Path.Combine(fullDestination, entryKey));
            if (!outPath.StartsWith(fullDestination, StringComparison.Ordinal))
            {
                throw new IOException($"Entry '{entryKey}' traverses outside destination directory.");
            }

            var parentDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            entry.WriteToFile(outPath, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true,
            });
            extractedFiles.Add(outPath);
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
