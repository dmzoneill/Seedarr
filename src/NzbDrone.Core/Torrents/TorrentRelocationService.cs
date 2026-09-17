using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class TorrentRelocationService : ITorrentRelocationService
{
    private const int BufferSize = 4 * 1024 * 1024; // 4MB buffer

    private readonly ITorrentService _torrentService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IConfigService _configService;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<int, TorrentRelocationProgress> _activeProgress = new();

    public bool ForceFallbackCopy { get; set; }

    public bool SimulateTruncation { get; set; }

    public TorrentRelocationService(
        ITorrentService torrentService,
        IEventAggregator eventAggregator = null,
        IConfigService configService = null)
    {
        _torrentService = torrentService ?? throw new ArgumentNullException(nameof(torrentService));
        _eventAggregator = eventAggregator;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public TorrentRelocationProgress GetProgress(int torrentId)
    {
        _activeProgress.TryGetValue(torrentId, out var progress);
        return progress;
    }

    public bool IsRelocating(int torrentId)
    {
        return _activeProgress.TryGetValue(torrentId, out var p) && !p.IsComplete;
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "File paths are validated and used for internal torrent relocation")]
    public async Task<bool> RelocateTorrentAsync(int torrentId, string newSavePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newSavePath))
        {
            throw new ArgumentException("Destination path must not be empty", nameof(newSavePath));
        }

        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            _logger.Warn("Torrent {0} not found for relocation", torrentId);
            return false;
        }

        var currentPath = !string.IsNullOrWhiteSpace(torrent.SavePath) ? torrent.SavePath : torrent.SourcePath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            currentPath = _configService?.DefaultSavePath ?? string.Empty;
        }

        var normalizedCurrent = Path.GetFullPath(currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var normalizedNew = Path.GetFullPath(newSavePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (string.Equals(normalizedCurrent, normalizedNew, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Debug("Torrent {0} is already at {1}", torrentId, newSavePath);
            return true;
        }

        // Determine source item (file or directory)
        string sourceItem = null;
        string destinationItem = null;
        var isDirectory = false;

        if (Directory.Exists(normalizedCurrent))
        {
            var torrentSubDir = Path.Combine(normalizedCurrent, torrent.Name ?? string.Empty);
            var torrentSubFile = Path.Combine(normalizedCurrent, torrent.Name ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(torrent.Name) && Directory.Exists(torrentSubDir))
            {
                sourceItem = torrentSubDir;
                destinationItem = Path.Combine(normalizedNew, torrent.Name);
                isDirectory = true;
            }
            else if (!string.IsNullOrWhiteSpace(torrent.Name) && File.Exists(torrentSubFile))
            {
                sourceItem = torrentSubFile;
                destinationItem = Path.Combine(normalizedNew, torrent.Name);
                isDirectory = false;
            }
            else
            {
                sourceItem = normalizedCurrent;
                destinationItem = normalizedNew;
                isDirectory = true;
            }
        }
        else if (File.Exists(normalizedCurrent))
        {
            sourceItem = normalizedCurrent;
            var fileName = Path.GetFileName(normalizedCurrent);
            destinationItem = Directory.Exists(normalizedNew) ? Path.Combine(normalizedNew, fileName) : normalizedNew;
            isDirectory = false;
        }
        else if (!string.IsNullOrWhiteSpace(torrent.SourcePath) && File.Exists(torrent.SourcePath))
        {
            sourceItem = torrent.SourcePath;
            var fileName = Path.GetFileName(torrent.SourcePath);
            destinationItem = Directory.Exists(normalizedNew) ? Path.Combine(normalizedNew, fileName) : normalizedNew;
            isDirectory = false;
        }

        if (sourceItem == null)
        {
            _logger.Warn("Torrent {0} source path not found on disk: {1}", torrentId, currentPath);
            torrent.SavePath = newSavePath;
            torrent.SourcePath = newSavePath;
            _torrentService.Update(torrent);
            return true;
        }

        // 1. Fast atomic move first: attempt File.Move / Directory.Move
        if (!ForceFallbackCopy)
        {
            try
            {
                var destParent = isDirectory
                    ? Path.GetDirectoryName(destinationItem.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : Path.GetDirectoryName(destinationItem);

                if (!string.IsNullOrWhiteSpace(destParent) && !Directory.Exists(destParent))
                {
                    Directory.CreateDirectory(destParent);
                }

                if (isDirectory)
                {
                    Directory.Move(sourceItem, destinationItem);
                }
                else
                {
                    File.Move(sourceItem, destinationItem);
                }

                _logger.Info("Fast atomic move completed for torrent {0} to {1}", torrentId, destinationItem);
                CompleteRelocation(torrent, currentPath, newSavePath);
                return true;
            }
            catch (IOException ex) when (IsCrossDeviceException(ex))
            {
                _logger.Info(ex, "Atomic move failed with cross-device link for torrent {0}, falling back to streaming copy-verify-delete", torrentId);
            }
        }

        // 2. Fallback to streaming copy-verify-delete
        return await ExecuteCopyVerifyDeleteAsync(torrent, sourceItem, destinationItem, isDirectory, currentPath, newSavePath, cancellationToken).ConfigureAwait(false);
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "File paths are validated and used for internal torrent relocation")]
    private async Task<bool> ExecuteCopyVerifyDeleteAsync(
        Torrent torrent,
        string sourceItem,
        string destinationItem,
        bool isDirectory,
        string currentPath,
        string newSavePath,
        CancellationToken cancellationToken)
    {
        var torrentId = torrent.Id;
        string[] allFiles;
        if (isDirectory)
        {
            allFiles = Directory.GetFiles(sourceItem, "*", SearchOption.AllDirectories);
        }
        else
        {
            allFiles = new[] { sourceItem };
        }

        var totalBytes = allFiles.Sum(f => new FileInfo(f).Length);
        var progress = new TorrentRelocationProgress
        {
            TorrentId = torrentId,
            TotalBytes = totalBytes,
            TotalFiles = allFiles.Length,
            BytesTransferred = 0,
            StartTimeUtc = DateTime.UtcNow
        };
        _activeProgress[torrentId] = progress;

        string currentTargetFile = null;
        var copiedFiles = new ConcurrentBag<(string Source, string Target)>();

        try
        {
            for (var i = 0; i < allFiles.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourceFile = allFiles[i];
                string targetFile;
                if (isDirectory)
                {
                    var rel = Path.GetRelativePath(sourceItem, sourceFile);
                    targetFile = Path.Combine(destinationItem, rel);
                }
                else
                {
                    targetFile = destinationItem;
                }

                currentTargetFile = targetFile;
                var targetDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                var sourceInfo = new FileInfo(sourceFile);

                // Chunked buffered copy with FileOptions.Asynchronous and FileOptions.SequentialScan
                var buffer = new byte[BufferSize];
                using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var destStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    int bytesRead;
                    while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        if (SimulateTruncation)
                        {
                            // Truncate write for testing integrity check
                            break;
                        }

                        await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                        progress.BytesTransferred += bytesRead;
                        UpdateSpeed(progress);
                        PublishProgress(progress, sourceFile, i + 1, allFiles.Length);
                    }

                    await destStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                // Verify integrity post-copy (e.g. target file length matches source file length)
                var targetInfo = new FileInfo(targetFile);
                if (targetInfo.Length != sourceInfo.Length)
                {
                    _logger.Error("Integrity verification failed for {0}: expected {1} bytes, got {2} bytes", targetFile, sourceInfo.Length, targetInfo.Length);
                    if (File.Exists(targetFile))
                    {
                        File.Delete(targetFile);
                    }

                    progress.ErrorMessage = $"Integrity verification failed: size mismatch ({targetInfo.Length} != {sourceInfo.Length})";
                    PublishProgress(progress, sourceFile, i + 1, allFiles.Length, isComplete: true);
                    _eventAggregator?.PublishEvent(new FileMoveFailedEvent(torrent, currentPath, newSavePath, progress.ErrorMessage));
                    return false;
                }

                // Preserve file timestamps and permissions
                File.SetCreationTimeUtc(targetFile, sourceInfo.CreationTimeUtc);
                File.SetLastWriteTimeUtc(targetFile, sourceInfo.LastWriteTimeUtc);
                File.SetLastAccessTimeUtc(targetFile, sourceInfo.LastAccessTimeUtc);

                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        File.SetUnixFileMode(targetFile, sourceInfo.UnixFileMode);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Failed to set UnixFileMode on {0}", targetFile);
                    }
                }

                copiedFiles.Add((sourceFile, targetFile));
            }

            // Delete source files only after all files are copied and verified
            foreach (var file in allFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            if (isDirectory && Directory.Exists(sourceItem))
            {
                try
                {
                    Directory.Delete(sourceItem, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to remove source directory {0}", sourceItem);
                }
            }

            CompleteRelocation(torrent, currentPath, newSavePath);
            progress.IsComplete = true;
            progress.Progress = 1.0;
            PublishProgress(progress, null, allFiles.Length, allFiles.Length, isComplete: true);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Relocation for torrent {0} was canceled", torrentId);
            if (!string.IsNullOrEmpty(currentTargetFile) && File.Exists(currentTargetFile))
            {
                try
                {
                    File.Delete(currentTargetFile);
                }
                catch
                {
                }
            }

            progress.ErrorMessage = "Relocation canceled";
            PublishProgress(progress, null, 0, allFiles.Length, isComplete: true);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Relocation for torrent {0} failed", torrentId);
            if (!string.IsNullOrEmpty(currentTargetFile) && File.Exists(currentTargetFile))
            {
                try
                {
                    File.Delete(currentTargetFile);
                }
                catch
                {
                }
            }

            progress.ErrorMessage = ex.Message;
            PublishProgress(progress, null, 0, allFiles.Length, isComplete: true);
            _eventAggregator?.PublishEvent(new FileMoveFailedEvent(torrent, currentPath, newSavePath, ex.Message));
            return false;
        }
        finally
        {
            _activeProgress.TryRemove(torrentId, out _);
        }
    }

    private void CompleteRelocation(Torrent torrent, string oldPath, string newPath)
    {
        torrent.SavePath = newPath;
        torrent.SourcePath = newPath;
        _torrentService.Update(torrent);
        _eventAggregator?.PublishEvent(new FileMoveCompletedEvent(torrent, oldPath, newPath));
    }

    private static void UpdateSpeed(TorrentRelocationProgress progress)
    {
        var elapsed = (DateTime.UtcNow - progress.StartTimeUtc).TotalSeconds;
        if (elapsed > 0)
        {
            progress.BytesPerSecond = progress.BytesTransferred / elapsed;
        }
    }

    private void PublishProgress(
        TorrentRelocationProgress progress,
        string currentFile,
        int currentFileIndex,
        int totalFiles,
        bool isComplete = false)
    {
        _eventAggregator?.PublishEvent(new TorrentRelocationProgressEvent(
            progress.TorrentId,
            progress.BytesTransferred,
            progress.TotalBytes,
            progress.Progress,
            progress.BytesPerSecond,
            currentFile,
            currentFileIndex,
            totalFiles,
            isComplete,
            progress.ErrorMessage));
    }

    public static bool IsCrossDeviceException(IOException ex)
    {
        if (ex == null)
        {
            return false;
        }

        var hr = ex.HResult & 0xFFFF;
        if (hr == 18 || hr == 17)
        {
            return true;
        }

        var msg = ex.Message;
        return msg.Contains("cross-device", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("EXDEV", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("different volume", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("different device", StringComparison.OrdinalIgnoreCase);
    }
}
