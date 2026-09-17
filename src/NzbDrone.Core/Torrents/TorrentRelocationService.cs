using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Torrents;

public enum RelocationMoveType
{
    AtomicMove,
    CopyDelete,
}

public class RelocationJournalEntry
{
    public string SourcePath { get; set; }

    public string DestinationPath { get; set; }

    public RelocationMoveType MoveType { get; set; }

    public bool SourceDeleted { get; set; }

    public bool DestinationCreated { get; set; }
}

public class TorrentRelocationService : ITorrentRelocationService
{
    private const int BufferSize = 4 * 1024 * 1024; // 4MB buffer

    private readonly ITorrentService _torrentService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IConfigService _configService;
    private readonly IConnectionManager _connectionManager;
    private readonly IPeerServer _peerServer;
    private readonly IPieceStorage _pieceStorage;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<int, TorrentRelocationProgress> _activeProgress = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _torrentLocks = new();

    public IDiskProvider DiskProvider { get; set; }

    public bool ForceFallbackCopy { get; set; }

    public bool SimulateTruncation { get; set; }

    public int? SimulateFailureOnFileIndex { get; set; }

    public Func<string, Task> BeforeFileCopyHook { get; set; }

    public TorrentRelocationService(
        ITorrentService torrentService,
        IEventAggregator eventAggregator = null,
        IConfigService configService = null,
        IConnectionManager connectionManager = null,
        IPeerServer peerServer = null,
        IPieceStorage pieceStorage = null,
        IDiskProvider diskProvider = null)
    {
        _torrentService = torrentService ?? throw new ArgumentNullException(nameof(torrentService));
        _eventAggregator = eventAggregator;
        _configService = configService;
        _connectionManager = connectionManager;
        _peerServer = peerServer;
        _pieceStorage = pieceStorage;
        DiskProvider = diskProvider ?? new DiskProvider();
        _logger = LogManager.GetCurrentClassLogger();
    }

    public SemaphoreSlim GetTorrentLock(int torrentId)
    {
        return _torrentLocks.GetOrAdd(torrentId, _ => new SemaphoreSlim(1, 1));
    }

    public bool IsLocked(int torrentId)
    {
        return _torrentLocks.TryGetValue(torrentId, out var sem) && sem.CurrentCount == 0;
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

        if (PathSanitizer.ContainsPathTraversal(newSavePath) || !PathSanitizer.IsValidPath(newSavePath))
        {
            throw new ArgumentException($"Destination path is invalid: {newSavePath}", nameof(newSavePath));
        }

        var torrentLock = GetTorrentLock(torrentId);
        await torrentLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        var statusTransitioned = false;
        Torrent torrent = null;
        var previousStatus = TorrentStatus.Paused;
        string currentPath = null;
        var createdDirectories = new List<string>();

        try
        {
            torrent = _torrentService.Get(torrentId);
            if (torrent == null)
            {
                _logger.Warn("Torrent {0} not found for relocation", torrentId);
                return false;
            }

            currentPath = !string.IsNullOrWhiteSpace(torrent.SavePath) ? torrent.SavePath : torrent.SourcePath;
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
                CompleteRelocation(torrent, currentPath, newSavePath, previousStatus);
                UnchokePeers(torrent);
                statusTransitioned = false;
                return true;
            }

            // Pre-flight disk space & write permission validation
            var targetDirectory = isDirectory
                ? Path.GetDirectoryName(destinationItem.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : Path.GetDirectoryName(destinationItem);

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                targetDirectory = normalizedNew;
            }

            long totalPayloadSize = 0;
            if (isDirectory)
            {
                var files = Directory.GetFiles(sourceItem, "*", SearchOption.AllDirectories);
                totalPayloadSize = files.Sum(f => new FileInfo(f).Length);
            }
            else
            {
                totalPayloadSize = new FileInfo(sourceItem).Length;
            }

            var availableFreeSpace = DiskProvider.GetAvailableFreeSpace(targetDirectory);
            if (availableFreeSpace < totalPayloadSize)
            {
                var errorMsg = $"Insufficient free space on destination volume for torrent {torrentId}: required {totalPayloadSize} bytes, available {availableFreeSpace} bytes";
                _logger.Error(errorMsg);
                _eventAggregator?.PublishEvent(new FileMoveFailedEvent(torrent, currentPath, newSavePath, errorMsg));
                return false;
            }

            if (!DiskProvider.CheckFolderWritable(targetDirectory))
            {
                var errorMsg = $"Destination directory '{targetDirectory}' is not writable or access is denied";
                _logger.Error(errorMsg);
                _eventAggregator?.PublishEvent(new FileMoveFailedEvent(torrent, currentPath, newSavePath, errorMsg));
                return false;
            }

            // 1. State transition & peer choking
            previousStatus = torrent.Status;
            torrent.Status = TorrentStatus.Moving;
            _torrentService.Update(torrent);
            _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, previousStatus, TorrentStatus.Moving));
            statusTransitioned = true;

            ChokePeers(torrent);

            // 2. Teardown handles & flush buffers
            TeardownHandlesAndFlushBuffers(torrent);

            // 3. Fast atomic move first: attempt File.Move / Directory.Move
            if (!ForceFallbackCopy)
            {
                try
                {
                    var destParent = isDirectory
                        ? Path.GetDirectoryName(destinationItem.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                        : Path.GetDirectoryName(destinationItem);

                    if (!string.IsNullOrWhiteSpace(destParent) && !Directory.Exists(destParent))
                    {
                        TrackAndCreateDirectory(destParent, createdDirectories);
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
                    CompleteRelocation(torrent, currentPath, newSavePath, previousStatus);
                    UnchokePeers(torrent);
                    statusTransitioned = false;
                    return true;
                }
                catch (IOException ex) when (IsCrossDeviceException(ex))
                {
                    _logger.Info(ex, "Atomic move failed with cross-device link for torrent {0}, falling back to streaming copy-verify-delete", torrentId);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Fast atomic move failed for torrent {0}", torrentId);
                    RollbackCreatedDirectories(createdDirectories);
                    throw;
                }
            }

            // 4. Fallback to streaming copy-verify-delete
            var success = await ExecuteCopyVerifyDeleteAsync(
                torrent,
                sourceItem,
                destinationItem,
                isDirectory,
                currentPath,
                newSavePath,
                previousStatus,
                createdDirectories,
                cancellationToken).ConfigureAwait(false);

            if (!success)
            {
                torrent.SavePath = currentPath;
                torrent.SourcePath = currentPath;
                RestoreStatusOnFailure(torrent, previousStatus);
            }

            UnchokePeers(torrent);
            statusTransitioned = false;
            return success;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Relocation failed for torrent {0}", torrentId);
            RollbackCreatedDirectories(createdDirectories);
            if (statusTransitioned && torrent != null)
            {
                torrent.SavePath = currentPath;
                torrent.SourcePath = currentPath;
                RestoreStatusOnFailure(torrent, previousStatus);
                UnchokePeers(torrent);
                statusTransitioned = false;
            }

            return false;
        }
        finally
        {
            torrentLock.Release();
        }
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "File paths are validated and used for internal torrent relocation")]
    private async Task<bool> ExecuteCopyVerifyDeleteAsync(
        Torrent torrent,
        string sourceItem,
        string destinationItem,
        bool isDirectory,
        string currentPath,
        string newSavePath,
        TorrentStatus previousStatus,
        List<string> createdDirectories,
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
            StartTimeUtc = DateTime.UtcNow,
        };
        _activeProgress[torrentId] = progress;

        string currentTargetFile = null;
        var rollbackJournal = new Stack<RelocationJournalEntry>();

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
                    TrackAndCreateDirectory(targetDir, createdDirectories);
                }

                if (SimulateFailureOnFileIndex.HasValue && SimulateFailureOnFileIndex.Value == i)
                {
                    throw new IOException($"Simulated relocation failure on file index {i} ({targetFile})");
                }

                if (BeforeFileCopyHook != null)
                {
                    await BeforeFileCopyHook(targetFile).ConfigureAwait(false);
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
                    throw new IOException($"Integrity verification failed for {targetFile}: expected {sourceInfo.Length} bytes, got {targetInfo.Length} bytes");
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

                rollbackJournal.Push(new RelocationJournalEntry
                {
                    SourcePath = sourceFile,
                    DestinationPath = targetFile,
                    MoveType = RelocationMoveType.CopyDelete,
                    SourceDeleted = false,
                    DestinationCreated = true,
                });
                currentTargetFile = null;
            }

            // Delete source files only after all files are copied and verified
            foreach (var entry in rollbackJournal)
            {
                if (File.Exists(entry.SourcePath))
                {
                    File.Delete(entry.SourcePath);
                    entry.SourceDeleted = true;
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

            CompleteRelocation(torrent, currentPath, newSavePath, previousStatus);
            progress.IsComplete = true;
            progress.Progress = 1.0;
            PublishProgress(progress, null, allFiles.Length, allFiles.Length, isComplete: true);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Relocation for torrent {0} was canceled", torrentId);
            ExecuteRollback(rollbackJournal, createdDirectories, currentTargetFile);
            progress.ErrorMessage = "Relocation canceled";
            PublishProgress(progress, null, 0, allFiles.Length, isComplete: true);
            _eventAggregator?.PublishEvent(new FileMoveFailedEvent(torrent, currentPath, newSavePath, "Relocation canceled"));
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Relocation for torrent {0} failed", torrentId);
            ExecuteRollback(rollbackJournal, createdDirectories, currentTargetFile);
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

    private void ExecuteRollback(
        Stack<RelocationJournalEntry> rollbackJournal,
        List<string> createdDirectories,
        string currentTargetFile)
    {
        _logger.Info("Executing rollback for relocation operations");

        if (!string.IsNullOrEmpty(currentTargetFile) && File.Exists(currentTargetFile))
        {
            try
            {
                File.Delete(currentTargetFile);
                _logger.Debug("Rollback: deleted partial file {0}", currentTargetFile);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Rollback: failed to delete partial file {0}", currentTargetFile);
            }
        }

        while (rollbackJournal != null && rollbackJournal.Count > 0)
        {
            var entry = rollbackJournal.Pop();
            try
            {
                if (entry.MoveType == RelocationMoveType.AtomicMove)
                {
                    if (File.Exists(entry.DestinationPath))
                    {
                        var srcDir = Path.GetDirectoryName(entry.SourcePath);
                        if (!string.IsNullOrEmpty(srcDir) && !Directory.Exists(srcDir))
                        {
                            Directory.CreateDirectory(srcDir);
                        }

                        File.Move(entry.DestinationPath, entry.SourcePath, overwrite: true);
                        _logger.Debug("Rollback: moved {0} back to {1}", entry.DestinationPath, entry.SourcePath);
                    }
                }
                else if (entry.MoveType == RelocationMoveType.CopyDelete)
                {
                    if (entry.SourceDeleted)
                    {
                        if (File.Exists(entry.DestinationPath))
                        {
                            var srcDir = Path.GetDirectoryName(entry.SourcePath);
                            if (!string.IsNullOrEmpty(srcDir) && !Directory.Exists(srcDir))
                            {
                                Directory.CreateDirectory(srcDir);
                            }

                            File.Move(entry.DestinationPath, entry.SourcePath, overwrite: true);
                            _logger.Debug("Rollback: restored deleted source {0} from {1}", entry.SourcePath, entry.DestinationPath);
                        }
                    }
                    else
                    {
                        if (File.Exists(entry.DestinationPath))
                        {
                            File.Delete(entry.DestinationPath);
                            _logger.Debug("Rollback: deleted destination copy {0}", entry.DestinationPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Rollback: failed to undo file operation for {0}", entry.DestinationPath);
            }
        }

        RollbackCreatedDirectories(createdDirectories);
    }

    private void RollbackCreatedDirectories(List<string> createdDirectories)
    {
        if (createdDirectories == null || createdDirectories.Count == 0)
        {
            return;
        }

        for (var i = createdDirectories.Count - 1; i >= 0; i--)
        {
            var dir = createdDirectories[i];
            try
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                {
                    Directory.Delete(dir);
                    _logger.Debug("Rollback: cleaned up empty directory {0}", dir);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Rollback: failed to clean up directory {0}", dir);
            }
        }
    }

    private static void TrackAndCreateDirectory(string path, List<string> createdDirectories)
    {
        if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path))
        {
            return;
        }

        var dirsToCreate = new List<string>();
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
        {
            dirsToCreate.Add(current);
            var parent = Path.GetDirectoryName(current);
            if (parent == current || string.IsNullOrEmpty(parent))
            {
                break;
            }

            current = parent;
        }

        dirsToCreate.Reverse();
        foreach (var dir in dirsToCreate)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                if (!createdDirectories.Contains(dir, StringComparer.OrdinalIgnoreCase))
                {
                    createdDirectories.Add(dir);
                }
            }
        }
    }

    private void CompleteRelocation(Torrent torrent, string oldPath, string newPath, TorrentStatus previousStatus)
    {
        torrent.SavePath = newPath;
        torrent.SourcePath = newPath;
        var restoredStatus = previousStatus != TorrentStatus.Moving ? previousStatus : TorrentStatus.Paused;
        torrent.Status = restoredStatus;
        _torrentService.Update(torrent);
        _eventAggregator?.PublishEvent(new FileMoveCompletedEvent(torrent, oldPath, newPath));
        _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, TorrentStatus.Moving, restoredStatus));
    }

    private void RestoreStatusOnFailure(Torrent torrent, TorrentStatus previousStatus)
    {
        var restoredStatus = previousStatus != TorrentStatus.Moving ? previousStatus : TorrentStatus.Error;
        torrent.Status = restoredStatus;
        _torrentService.Update(torrent);
        _logger.Warn("Torrent {0} status restored to {1} at {2}", torrent.Id, restoredStatus, torrent.SavePath);
        _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, TorrentStatus.Moving, restoredStatus));
    }

    private void ChokePeers(Torrent torrent)
    {
        if (torrent == null || string.IsNullOrEmpty(torrent.InfoHash))
        {
            return;
        }

        try
        {
            if (_peerServer != null)
            {
                _peerServer.ChokePeers(torrent.InfoHash);
            }

            if (_connectionManager != null && _peerServer == null)
            {
                var connections = _connectionManager.GetConnections(torrent.InfoHash);
                if (connections != null)
                {
                    foreach (var conn in connections)
                    {
                        if (conn != null && !conn.AmChoking)
                        {
                            conn.AmChoking = true;
                            conn.SendMessage(new PeerMessage { Type = PeerMessageType.Choke });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to choke peers for torrent {0} during relocation", torrent.Id);
        }
    }

    private void UnchokePeers(Torrent torrent)
    {
        if (torrent == null || string.IsNullOrEmpty(torrent.InfoHash))
        {
            return;
        }

        try
        {
            if (_peerServer != null)
            {
                _peerServer.UnchokePeers(torrent.InfoHash);
            }

            if (_connectionManager != null && _peerServer == null)
            {
                var connections = _connectionManager.GetConnections(torrent.InfoHash);
                if (connections != null)
                {
                    foreach (var conn in connections)
                    {
                        if (conn != null && conn.AmChoking)
                        {
                            conn.AmChoking = false;
                            conn.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to unchoke peers for torrent {0} after relocation", torrent.Id);
        }
    }

    private void TeardownHandlesAndFlushBuffers(Torrent torrent)
    {
        if (torrent == null)
        {
            return;
        }

        try
        {
            if (_pieceStorage != null)
            {
                _pieceStorage.Flush();
                if (!string.IsNullOrEmpty(torrent.InfoHash))
                {
                    _pieceStorage.CloseHandles(torrent.InfoHash);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to teardown handles and flush buffers for torrent {0}", torrent.Id);
        }
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
