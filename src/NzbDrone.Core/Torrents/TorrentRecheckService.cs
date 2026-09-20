using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Seeding;
using NzbDrone.SignalR;

namespace NzbDrone.Core.Torrents;

public interface ITorrentRecheckService
{
    Torrent Recheck(int id, byte[] pieceHashes = null);
    Torrent Recheck(Torrent torrent, byte[] pieceHashes = null);
    Task<Torrent> RecheckAsync(int id, CancellationToken cancellationToken = default);
    Task<Torrent> RecheckAsync(Torrent torrent, byte[] pieceHashes = null, CancellationToken cancellationToken = default);
    Torrent QueueRecheck(int id);
    void CancelRecheck(int id);
}

public class TorrentRecheckService : ITorrentRecheckService
{
    private readonly ITorrentRepository _torrentRepository;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IPieceStorage _pieceStorage;
    private readonly IPieceVerificationService _pieceVerificationService;
    private readonly IMultiFilePieceStorage _multiFilePieceStorage;
    private readonly ITorrentStateMachine _stateMachine;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly IFastResumeService _fastResumeService;
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly Logger _logger;

    private readonly SemaphoreSlim _recheckConcurrencySemaphore = new(1, 1);
    private readonly SemaphoreSlim _queueProcessingLock = new(1, 1);
    private readonly ConcurrentQueue<int> _recheckQueue = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeRechecks = new();
    private readonly ConcurrentDictionary<int, bool> _queuedTorrentIds = new();

    public TorrentRecheckService(
        ITorrentRepository torrentRepository,
        ITorrentFileService torrentFileService,
        IPieceStorage pieceStorage,
        IPieceVerificationService pieceVerificationService,
        IMultiFilePieceStorage multiFilePieceStorage = null,
        ITorrentStateMachine stateMachine = null,
        IEventAggregator eventAggregator = null,
        ITorrentFileParser torrentFileParser = null,
        IFastResumeService fastResumeService = null,
        IBroadcastSignalRMessage signalRBroadcaster = null)
    {
        _torrentRepository = torrentRepository;
        _torrentFileService = torrentFileService;
        _pieceStorage = pieceStorage;
        _pieceVerificationService = pieceVerificationService;
        _multiFilePieceStorage = multiFilePieceStorage ?? new MultiFilePieceStorage();
        _stateMachine = stateMachine;
        _eventAggregator = eventAggregator;
        _torrentFileParser = torrentFileParser ?? new TorrentFileParser();
        _fastResumeService = fastResumeService;
        _signalRBroadcaster = signalRBroadcaster;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public Torrent QueueRecheck(int id)
    {
        var torrent = _torrentRepository?.Get(id);
        if (torrent == null)
        {
            return null;
        }

        if (_activeRechecks.ContainsKey(id) || _queuedTorrentIds.ContainsKey(id))
        {
            _logger.Info("Torrent {0} (Id: {1}) is already queued or active for recheck", torrent.Name, torrent.Id);
            return torrent;
        }

        var oldStatus = torrent.Status;
        torrent.Status = TorrentStatus.QueuedForChecking;
        torrent.Active = false;
        torrent.UploadSpeed = 0;
        torrent.DownloadSpeed = 0;

        _torrentRepository?.Update(torrent);
        _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.QueuedForChecking));
        _eventAggregator?.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));

        if (_signalRBroadcaster != null)
        {
            var updateMsg = new SignalRMessage
            {
                Name = "TorrentUpdated",
                Action = ModelAction.Updated,
                Body = torrent
            };
            _signalRBroadcaster.BroadcastMessage(updateMsg);
            _signalRBroadcaster.BroadcastToTorrent(torrent.Id, updateMsg);
        }

        _recheckQueue.Enqueue(id);
        _queuedTorrentIds[id] = true;

        _ = Task.Run(ProcessQueueAsync);

        return torrent;
    }

    public void CancelRecheck(int id)
    {
        _queuedTorrentIds.TryRemove(id, out _);

        if (_activeRechecks.TryGetValue(id, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task ProcessQueueAsync()
    {
        if (!await _queueProcessingLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            while (_recheckQueue.TryDequeue(out var nextId))
            {
                if (!_queuedTorrentIds.TryRemove(nextId, out _))
                {
                    continue;
                }

                var torrent = _torrentRepository?.Get(nextId);
                if (torrent == null)
                {
                    continue;
                }

                using var cts = new CancellationTokenSource();
                _activeRechecks[nextId] = cts;

                try
                {
                    await _recheckConcurrencySemaphore.WaitAsync(cts.Token).ConfigureAwait(false);
                    try
                    {
                        await ExecuteRecheckCoreAsync(torrent, null, cts.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _recheckConcurrencySemaphore.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.Info("Recheck canceled for torrent {0} (Id: {1})", torrent.Name, torrent.Id);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during recheck for torrent {0} (Id: {1})", torrent.Name, torrent.Id);
                }
                finally
                {
                    _activeRechecks.TryRemove(nextId, out _);
                }
            }
        }
        finally
        {
            _queueProcessingLock.Release();
        }
    }

    public async Task<Torrent> RecheckAsync(int id, CancellationToken cancellationToken = default)
    {
        var torrent = _torrentRepository?.Get(id);
        if (torrent == null)
        {
            return null;
        }

        return await RecheckAsync(torrent, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Torrent> RecheckAsync(Torrent torrent, byte[] pieceHashes = null, CancellationToken cancellationToken = default)
    {
        if (torrent == null)
        {
            return null;
        }

        try
        {
            await _recheckConcurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ExecuteRecheckCoreAsync(torrent, pieceHashes, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _recheckConcurrencySemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Recheck canceled for torrent {0} (Id: {1})", torrent.Name, torrent.Id);
            torrent.Status = TorrentStatus.Paused;
            _torrentRepository?.Update(torrent);
            throw;
        }
    }

    public Torrent Recheck(int id, byte[] pieceHashes = null)
    {
        var torrent = _torrentRepository?.Get(id);
        if (torrent == null)
        {
            return null;
        }

        return Recheck(torrent, pieceHashes);
    }

    public Torrent Recheck(Torrent torrent, byte[] pieceHashes = null)
    {
        if (torrent == null)
        {
            return null;
        }

        _recheckConcurrencySemaphore.Wait();
        try
        {
            return ExecuteRecheckCoreAsync(torrent, pieceHashes, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            _recheckConcurrencySemaphore.Release();
        }
    }

    private Task<Torrent> ExecuteRecheckCoreAsync(Torrent torrent, byte[] pieceHashes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.Info("Starting hash verification for torrent {0} (Id: {1})", torrent.Name, torrent.Id);

        // 1. Suspend active transfer speeds and active flags before hash verification
        torrent.Active = false;
        torrent.UploadSpeed = 0;
        torrent.DownloadSpeed = 0;

        // 2. Coordinate with TorrentStateMachine / SeedingEngine to enter Checking status
        var oldStatus = torrent.Status;
        if (_stateMachine != null)
        {
            _stateMachine.TransitionToChecking(torrent);
        }
        else
        {
            torrent.Status = TorrentStatus.Checking;
            _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, oldStatus, TorrentStatus.Checking));
        }

        _torrentRepository?.Update(torrent);
        _eventAggregator?.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));

        if (_signalRBroadcaster != null)
        {
            var updateMsg = new SignalRMessage
            {
                Name = "TorrentUpdated",
                Action = ModelAction.Updated,
                Body = torrent
            };
            _signalRBroadcaster.BroadcastMessage(updateMsg);
            _signalRBroadcaster.BroadcastToTorrent(torrent.Id, updateMsg);
        }

        try
        {
            // 3. Resolve files and expected piece hashes
            var files = _torrentFileService?.GetByTorrentId(torrent.Id) ?? torrent.Files;
            if ((files == null || files.Count == 0) && torrent.TotalSize > 0)
            {
                files = new List<TorrentFile>
                {
                    new()
                    {
                        TorrentId = torrent.Id,
                        Path = torrent.Name ?? "file",
                        Size = torrent.TotalSize
                    }
                };
            }

            if (pieceHashes == null && !string.IsNullOrWhiteSpace(torrent.SourcePath) && File.Exists(torrent.SourcePath))
            {
                try
                {
                    using var stream = new FileStream(torrent.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var parsed = _torrentFileParser?.Parse(stream);
                    if (parsed?.Pieces != null)
                    {
                        pieceHashes = parsed.Pieces;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to parse piece hashes from source torrent file for {0}", torrent.Name);
                }
            }

            var pieceCount = torrent.PieceCount;
            if (pieceCount <= 0 && pieceHashes != null)
            {
                pieceCount = pieceHashes.Length / 20;
                torrent.PieceCount = pieceCount;
            }

            if (pieceCount <= 0)
            {
                pieceCount = 1;
            }

            var bitfield = new bool[pieceCount];
            var lastPublishedProgress = -1.0;
            var lastPublishTime = DateTime.MinValue;

            // 4. Perform piece verification sequentially with thread-safe file isolation
            for (var i = 0; i < pieceCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (pieceHashes != null && pieceHashes.Length >= (i + 1) * 20 && files != null && files.Count > 0 && _pieceVerificationService != null)
                    {
                        var expectedHash = new byte[20];
                        Array.Copy(pieceHashes, i * 20, expectedHash, 0, 20);
                        var verified = _pieceVerificationService.VerifyPieceFromStorage(
                            torrent,
                            files,
                            i,
                            expectedHash,
                            _multiFilePieceStorage,
                            torrent.SavePath);

                        bitfield[i] = verified;
                    }
                    else
                    {
                        bitfield[i] = VerifyPiecePresenceFallback(torrent, files, i, pieceCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error verifying piece {0} for torrent {1}", i, torrent.Id);
                    bitfield[i] = false;
                }

                var checkedPieces = i + 1;
                var progress = (double)checkedPieces / pieceCount;
                var now = DateTime.UtcNow;

                // Throttled: every 200ms or 1% (0.01) delta, or first / last piece
                if (i == 0 || checkedPieces == pieceCount || (progress - lastPublishedProgress) >= 0.01 || (now - lastPublishTime).TotalMilliseconds >= 200)
                {
                    lastPublishedProgress = progress;
                    lastPublishTime = now;

                    _eventAggregator?.PublishEvent(new TorrentRecheckProgressEvent(torrent.Id, progress, checkedPieces, pieceCount));

                    if (_signalRBroadcaster != null)
                    {
                        var progressMsg = new SignalRMessage
                        {
                            Name = "TorrentRecheckProgress",
                            Body = new
                            {
                                torrentId = torrent.Id,
                                progress,
                                checkedPieces,
                                totalPieces = pieceCount
                            }
                        };
                        _signalRBroadcaster.BroadcastMessage(progressMsg);
                        _signalRBroadcaster.BroadcastToTorrent(torrent.Id, progressMsg);
                    }
                }
            }

            // 5. Update torrent bitfield and verified pieces in IPieceStorage
            if (_pieceStorage != null && !string.IsNullOrEmpty(torrent.InfoHash))
            {
                _pieceStorage.SetVerifiedPieces(torrent.InfoHash, bitfield);
            }

            var verifiedCount = bitfield.Count(b => b);
            torrent.Progress = pieceCount > 0 ? (double)verifiedCount / pieceCount : 0.0;
            if (verifiedCount == pieceCount)
            {
                torrent.Progress = 1.0;
            }

            // 6. Transition state back to appropriate status (Downloading or Seeding)
            if (_stateMachine != null)
            {
                _stateMachine.TransitionFromChecking(torrent);
            }
            else
            {
                var checkingStatus = torrent.Status;
                var newStatus = torrent.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
                torrent.Status = newStatus;
                _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, checkingStatus, newStatus));
            }

            torrent.LastActive = DateTime.UtcNow;
            _torrentRepository?.Update(torrent);
            _eventAggregator?.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));

            if (_signalRBroadcaster != null)
            {
                var finalMsg = new SignalRMessage
                {
                    Name = "TorrentUpdated",
                    Action = ModelAction.Updated,
                    Body = torrent
                };
                _signalRBroadcaster.BroadcastMessage(finalMsg);
                _signalRBroadcaster.BroadcastToTorrent(torrent.Id, finalMsg);
            }

            try
            {
                _fastResumeService?.SaveFastResume(torrent);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to save fast resume after recheck for {0}", torrent.InfoHash);
            }

            _logger.Info(
                "Recheck completed for torrent {0} (Id: {1}): {2}/{3} pieces verified, status {4}",
                torrent.Name,
                torrent.Id,
                verifiedCount,
                pieceCount,
                torrent.Status);

            return Task.FromResult(torrent);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Hash verification cancelled for torrent {0} (Id: {1})", torrent.Name, torrent.Id);
            var statusBeforeCancel = torrent.Status;
            torrent.Status = TorrentStatus.Paused;
            _torrentRepository?.Update(torrent);
            _eventAggregator?.PublishEvent(new TorrentStatusChangedEvent(torrent, statusBeforeCancel, TorrentStatus.Paused));
            _eventAggregator?.PublishEvent(new ModelEvent<Torrent>(torrent, ModelAction.Updated));

            if (_signalRBroadcaster != null)
            {
                var cancelMsg = new SignalRMessage
                {
                    Name = "TorrentUpdated",
                    Action = ModelAction.Updated,
                    Body = torrent
                };
                _signalRBroadcaster.BroadcastMessage(cancelMsg);
                _signalRBroadcaster.BroadcastToTorrent(torrent.Id, cancelMsg);
            }

            throw;
        }
    }

    private static bool VerifyPiecePresenceFallback(Torrent torrent, IList<TorrentFile> files, int pieceIndex, int pieceCount)
    {
        if (files == null || files.Count == 0)
        {
            return torrent.Progress >= 1.0;
        }

        var pieceLength = torrent.PieceLength > 0 ? torrent.PieceLength : (int)Math.Max(1, torrent.TotalSize / pieceCount);
        var pieceStart = (long)pieceIndex * pieceLength;
        var pieceEnd = Math.Min(torrent.TotalSize, pieceStart + pieceLength);

        var basePath = !string.IsNullOrWhiteSpace(torrent.SavePath) ? torrent.SavePath : string.Empty;
        long currentOffset = 0;

        foreach (var file in files)
        {
            var fileEnd = currentOffset + file.Size;
            if (currentOffset < pieceEnd && fileEnd > pieceStart)
            {
                var diskPath = Path.IsPathRooted(file.Path) ? file.Path : Path.Combine(basePath, file.Path);
                if (!File.Exists(diskPath))
                {
                    return false;
                }

                try
                {
                    using var handle = File.OpenHandle(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var fileLength = RandomAccess.GetLength(handle);
                    var neededInFile = Math.Min(file.Size, pieceEnd - currentOffset);
                    if (fileLength < neededInFile)
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            currentOffset = fileEnd;
        }

        return true;
    }
}
