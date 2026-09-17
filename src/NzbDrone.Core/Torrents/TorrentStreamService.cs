using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers.PiecePicker;

namespace NzbDrone.Core.Torrents;

public interface ITorrentStreamService
{
    void NotifyStreamPosition(int torrentId, long byteOffset, long pieceLength = 0);
    Task<bool> WaitForPieceAsync(int torrentId, int pieceIndex, TimeSpan timeout, CancellationToken cancellationToken = default);
    void NotifyPieceCompleted(int torrentId, int pieceIndex);
    void NotifyPieceCompleted(string infoHash, int pieceIndex);
    bool IsPieceAvailable(int torrentId, int pieceIndex);
    int? GetHeadPiece(int torrentId);
    StreamingPiecePicker PiecePicker { get; }
}

public class TorrentStreamService : ITorrentStreamService
{
    private readonly StreamingPiecePicker _piecePicker;
    private readonly ITorrentService _torrentService;
    private readonly IPieceStorage _pieceStorage;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<(int TorrentId, int PieceIndex), ConcurrentBag<TaskCompletionSource<bool>>> _waiters = new();

    public StreamingPiecePicker PiecePicker => _piecePicker;

    public TorrentStreamService(
        StreamingPiecePicker piecePicker = null,
        ITorrentService torrentService = null,
        IPieceStorage pieceStorage = null,
        IEventAggregator eventAggregator = null)
    {
        _piecePicker = piecePicker ?? new StreamingPiecePicker();
        _torrentService = torrentService;
        _pieceStorage = pieceStorage;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void NotifyStreamPosition(int torrentId, long byteOffset, long pieceLength = 0)
    {
        if (pieceLength <= 0)
        {
            var torrent = _torrentService?.Get(torrentId);
            pieceLength = torrent?.PieceLength ?? 262144;
        }

        if (pieceLength <= 0)
        {
            pieceLength = 262144;
        }

        var headPiece = (int)(byteOffset / pieceLength);
        _piecePicker.SetHeadPiece(torrentId, headPiece);

        _logger.Debug("Torrent {0} streaming position updated: offset={1}, headPiece={2}", torrentId, byteOffset, headPiece);

        _eventAggregator?.PublishEvent(new TorrentStreamPlayheadMovedEvent(torrentId, byteOffset, headPiece));
    }

    public int? GetHeadPiece(int torrentId)
    {
        return _piecePicker.GetHeadPiece(torrentId);
    }

    public bool IsPieceAvailable(int torrentId, int pieceIndex)
    {
        if (pieceIndex < 0)
        {
            return false;
        }

        var torrent = _torrentService?.Get(torrentId);
        if (torrent == null)
        {
            return false;
        }

        if (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding || torrent.ForceCompleted)
        {
            return true;
        }

        if (_pieceStorage != null && !string.IsNullOrEmpty(torrent.InfoHash))
        {
            return _pieceStorage.IsPieceVerified(torrent.InfoHash, pieceIndex);
        }

        return false;
    }

    public async Task<bool> WaitForPieceAsync(
        int torrentId,
        int pieceIndex,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (IsPieceAvailable(torrentId, pieceIndex))
        {
            return true;
        }

        if (timeout <= TimeSpan.Zero || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = (torrentId, pieceIndex);
        var bag = _waiters.GetOrAdd(key, _ => new ConcurrentBag<TaskCompletionSource<bool>>());
        bag.Add(tcs);

        if (IsPieceAvailable(torrentId, pieceIndex))
        {
            tcs.TrySetResult(true);
            return true;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            cts.CancelAfter(timeout);
        }

        using (cts.Token.Register(() => tcs.TrySetResult(false)))
        {
            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _waiters.TryRemove(key, out _);
            }
        }
    }

    public void NotifyPieceCompleted(int torrentId, int pieceIndex)
    {
        var key = (torrentId, pieceIndex);
        if (_waiters.TryRemove(key, out var bag))
        {
            foreach (var waiter in bag)
            {
                waiter.TrySetResult(true);
            }
        }
    }

    public void NotifyPieceCompleted(string infoHash, int pieceIndex)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        var torrent = _torrentService?.GetByInfoHash(infoHash);
        if (torrent != null)
        {
            NotifyPieceCompleted(torrent.Id, pieceIndex);
        }
    }
}
