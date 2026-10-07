using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NzbDrone.SignalR;

namespace NzbDrone.Core.Torrents;

public class PieceStorage : IPieceStorage, IDisposable
{
    private readonly ConcurrentDictionary<string, bool[]> _verifiedPieces = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HashSet<int>> _corruptedPieces = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingBatch> _pendingBatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly ITorrentService _torrentService;
    private readonly TimeSpan _coalesceWindow;
    private readonly object _stateLock = new();
    private bool _disposed;

    public int PendingBatchCount => _pendingBatches.Count;

    public PieceStorage(IBroadcastSignalRMessage signalRBroadcaster = null, TimeSpan? coalesceWindow = null, ITorrentService torrentService = null)
    {
        _signalRBroadcaster = signalRBroadcaster;
        _torrentService = torrentService;
        _coalesceWindow = coalesceWindow ?? TimeSpan.Zero;
    }

    public bool[] GetVerifiedPieces(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return null;
        }

        lock (_stateLock)
        {
            if (_verifiedPieces.TryGetValue(infoHash, out var pieces))
            {
                return (bool[])pieces.Clone();
            }

            return null;
        }
    }

    public bool IsPieceVerified(string infoHash, int pieceIndex)
    {
        if (string.IsNullOrEmpty(infoHash) || pieceIndex < 0)
        {
            return false;
        }

        lock (_stateLock)
        {
            return _verifiedPieces.TryGetValue(infoHash, out var pieces) &&
                pieceIndex < pieces.Length &&
                pieces[pieceIndex];
        }
    }

    public void MarkPieceVerified(string infoHash, int pieceIndex, long bytesDownloaded = 0)
    {
        if (string.IsNullOrEmpty(infoHash) || pieceIndex < 0)
        {
            return;
        }

        lock (_stateLock)
        {
            if (!_verifiedPieces.TryGetValue(infoHash, out var pieces))
            {
                pieces = new bool[pieceIndex + 1];
                pieces[pieceIndex] = true;
                _verifiedPieces[infoHash] = pieces;
            }
            else if (pieceIndex >= pieces.Length)
            {
                var newPieces = new bool[Math.Max(pieceIndex + 1, pieces.Length * 2)];
                Array.Copy(pieces, newPieces, pieces.Length);
                newPieces[pieceIndex] = true;
                _verifiedPieces[infoHash] = newPieces;
            }
            else
            {
                pieces[pieceIndex] = true;
            }

            if (_corruptedPieces.TryGetValue(infoHash, out var corrupted))
            {
                corrupted.Remove(pieceIndex);
            }
        }

        if (_signalRBroadcaster == null)
        {
            return;
        }

        if (_coalesceWindow <= TimeSpan.Zero)
        {
            PublishPieceSignalR(infoHash, new PieceCompletedMessage(infoHash, pieceIndex, bytesDownloaded));
            return;
        }

        QueueCoalescedPiece(infoHash, pieceIndex, bytesDownloaded);
    }

    public void MarkPiecesVerified(string infoHash, IEnumerable<int> pieceIndexes, long bytesDownloaded = 0)
    {
        if (string.IsNullOrEmpty(infoHash) || pieceIndexes == null)
        {
            return;
        }

        var indexList = pieceIndexes.Where(i => i >= 0).Distinct().ToList();
        if (indexList.Count == 0)
        {
            return;
        }

        var maxIndex = indexList.Max();

        lock (_stateLock)
        {
            if (!_verifiedPieces.TryGetValue(infoHash, out var pieces))
            {
                pieces = new bool[maxIndex + 1];
                _verifiedPieces[infoHash] = pieces;
            }
            else if (maxIndex >= pieces.Length)
            {
                var newPieces = new bool[Math.Max(maxIndex + 1, pieces.Length * 2)];
                Array.Copy(pieces, newPieces, pieces.Length);
                pieces = newPieces;
                _verifiedPieces[infoHash] = pieces;
            }

            foreach (var idx in indexList)
            {
                pieces[idx] = true;
            }

            if (_corruptedPieces.TryGetValue(infoHash, out var corrupted))
            {
                foreach (var idx in indexList)
                {
                    corrupted.Remove(idx);
                }
            }
        }

        if (_signalRBroadcaster == null)
        {
            return;
        }

        if (_coalesceWindow <= TimeSpan.Zero)
        {
            if (indexList.Count == 1)
            {
                PublishPieceSignalR(infoHash, new PieceCompletedMessage(infoHash, indexList[0], bytesDownloaded));
            }
            else
            {
                PublishPieceSignalR(infoHash, new PieceBatchCompletedMessage(infoHash, indexList, bytesDownloaded));
            }

            return;
        }

        QueueCoalescedPieces(infoHash, indexList, bytesDownloaded);
    }

    public void MarkPieceCorrupted(string infoHash, int pieceIndex)
    {
        if (string.IsNullOrEmpty(infoHash) || pieceIndex < 0)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_verifiedPieces.TryGetValue(infoHash, out var pieces) && pieceIndex < pieces.Length)
            {
                pieces[pieceIndex] = false;
            }

            if (!_corruptedPieces.TryGetValue(infoHash, out var corrupted))
            {
                corrupted = new HashSet<int>();
                _corruptedPieces[infoHash] = corrupted;
            }

            corrupted.Add(pieceIndex);
        }
    }

    public bool IsPieceCorrupted(string infoHash, int pieceIndex)
    {
        if (string.IsNullOrEmpty(infoHash) || pieceIndex < 0)
        {
            return false;
        }

        lock (_stateLock)
        {
            return _corruptedPieces.TryGetValue(infoHash, out var corrupted) && corrupted.Contains(pieceIndex);
        }
    }

    public HashSet<int> GetCorruptedPieces(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return new HashSet<int>();
        }

        lock (_stateLock)
        {
            if (_corruptedPieces.TryGetValue(infoHash, out var corrupted))
            {
                return new HashSet<int>(corrupted);
            }

            return new HashSet<int>();
        }
    }

    public void SetVerifiedPieces(string infoHash, bool[] pieces)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        if (_pendingBatches.TryRemove(infoHash, out var pendingBatch))
        {
            pendingBatch.Dispose();
        }

        lock (_stateLock)
        {
            if (pieces == null)
            {
                _verifiedPieces.TryRemove(infoHash, out _);
            }
            else
            {
                _verifiedPieces[infoHash] = (bool[])pieces.Clone();
            }
        }

        PublishPieceMapUpdatedSignalR(infoHash, pieces == null);
    }

    public int[] GetPieceStates(string infoHash, int pieceCount, double progress = 0.0, HashSet<int> activePieces = null)
    {
        if (pieceCount <= 0)
        {
            pieceCount = 100;
        }

        var states = new int[pieceCount];
        bool[] verified = null;
        HashSet<int> corrupted = null;

        lock (_stateLock)
        {
            if (!string.IsNullOrEmpty(infoHash))
            {
                if (_verifiedPieces.TryGetValue(infoHash, out var v))
                {
                    verified = (bool[])v.Clone();
                }

                if (_corruptedPieces.TryGetValue(infoHash, out var c))
                {
                    corrupted = new HashSet<int>(c);
                }
            }
        }

        if (verified != null && verified.Length > 0)
        {
            for (var i = 0; i < pieceCount; i++)
            {
                if (corrupted != null && corrupted.Contains(i))
                {
                    states[i] = 3;
                }
                else if (i < verified.Length && verified[i])
                {
                    states[i] = 2;
                }
                else if (activePieces != null && activePieces.Contains(i))
                {
                    states[i] = 1;
                }
                else
                {
                    states[i] = 0;
                }
            }
        }
        else if (progress >= 1.0)
        {
            for (var i = 0; i < pieceCount; i++)
            {
                if (corrupted != null && corrupted.Contains(i))
                {
                    states[i] = 3;
                }
                else
                {
                    states[i] = 2;
                }
            }
        }
        else
        {
            var completedCount = (int)(pieceCount * progress);
            for (var i = 0; i < pieceCount; i++)
            {
                if (corrupted != null && corrupted.Contains(i))
                {
                    states[i] = 3;
                }
                else if (activePieces != null && activePieces.Contains(i))
                {
                    states[i] = 1;
                }
                else
                {
                    states[i] = i < completedCount ? 2 : 0;
                }
            }
        }

        return states;
    }

    public void Clear(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        if (_pendingBatches.TryRemove(infoHash, out var batch))
        {
            batch.Dispose();
        }

        lock (_stateLock)
        {
            _verifiedPieces.TryRemove(infoHash, out _);
            _corruptedPieces.TryRemove(infoHash, out _);
        }

        PublishPieceMapClearedSignalR(infoHash);
    }

    public void Flush()
    {
        while (_pendingBatches.Count > 0)
        {
            foreach (var key in _pendingBatches.Keys.ToArray())
            {
                FlushBatch(key);
            }
        }
    }

    public void CloseHandles(string infoHash)
    {
        if (!string.IsNullOrEmpty(infoHash))
        {
            FlushBatch(infoHash);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Flush();
        _disposed = true;
        foreach (var batch in _pendingBatches.Values)
        {
            batch.Dispose();
        }

        _pendingBatches.Clear();
    }

    private void QueueCoalescedPiece(string infoHash, int pieceIndex, long bytesDownloaded)
    {
        QueueCoalescedPieces(infoHash, new[] { pieceIndex }, bytesDownloaded);
    }

    private void QueueCoalescedPieces(string infoHash, IReadOnlyList<int> pieceIndexes, long bytesDownloaded)
    {
        if (pieceIndexes == null || pieceIndexes.Count == 0)
        {
            return;
        }

        while (true)
        {
            if (_disposed)
            {
                return;
            }

            var batch = _pendingBatches.GetOrAdd(infoHash, h => new PendingBatch(h));
            lock (batch.SyncLock)
            {
                if (batch.IsDisposed)
                {
                    continue;
                }

                var addedAny = false;
                foreach (var pieceIndex in pieceIndexes)
                {
                    if (batch.PieceIndexes.Contains(pieceIndex))
                    {
                        continue;
                    }

                    batch.PieceIndexes.Add(pieceIndex);
                    addedAny = true;
                }

                if (addedAny)
                {
                    batch.BytesDownloaded += bytesDownloaded;
                }

                if (batch.PieceIndexes.Count > 0 && batch.Timer == null)
                {
                    batch.Timer = new Timer(_ => FlushBatch(infoHash), null, _coalesceWindow, Timeout.InfiniteTimeSpan);
                }

                break;
            }
        }
    }

    private void FlushBatch(string infoHash)
    {
        if (_disposed || !_pendingBatches.TryGetValue(infoHash, out var batch))
        {
            return;
        }

        List<int> indexesToBroadcast;
        long bytesToBroadcast;

        lock (batch.SyncLock)
        {
            if (batch.Timer != null)
            {
                batch.Timer.Dispose();
                batch.Timer = null;
            }

            if (batch.PieceIndexes.Count == 0)
            {
                if (_pendingBatches.TryRemove(new KeyValuePair<string, PendingBatch>(infoHash, batch)))
                {
                    batch.Dispose();
                }

                return;
            }

            indexesToBroadcast = new List<int>(batch.PieceIndexes);
            bytesToBroadcast = batch.BytesDownloaded;
            batch.PieceIndexes.Clear();
            batch.BytesDownloaded = 0;

            if (_pendingBatches.TryRemove(new KeyValuePair<string, PendingBatch>(infoHash, batch)))
            {
                batch.Dispose();
            }
        }

        if (_signalRBroadcaster == null || indexesToBroadcast.Count == 0)
        {
            return;
        }

        try
        {
            if (indexesToBroadcast.Count == 1)
            {
                PublishPieceSignalR(infoHash, new PieceCompletedMessage(infoHash, indexesToBroadcast[0], bytesToBroadcast));
            }
            else
            {
                PublishPieceSignalR(infoHash, new PieceBatchCompletedMessage(infoHash, indexesToBroadcast, bytesToBroadcast));
            }
        }
        catch
        {
            // Defensive guard against unhandled timer callback exceptions
        }
    }

    private void PublishPieceSignalR(string infoHash, SignalRMessage message)
    {
        if (_signalRBroadcaster == null || message == null)
        {
            return;
        }

        var torrentId = ResolveTorrentId(infoHash);
        if (message is PieceCompletedMessage pieceMessage && torrentId.HasValue)
        {
            pieceMessage.TorrentId = torrentId.Value;
        }
        else if (message is PieceBatchCompletedMessage batchMessage && torrentId.HasValue)
        {
            batchMessage.TorrentId = torrentId.Value;
        }
        else if (message is PieceMapUpdatedMessage mapMessage && torrentId.HasValue)
        {
            mapMessage.TorrentId = torrentId.Value;
        }

        SignalRBroadcastFanout.Broadcast(_signalRBroadcaster, message, "torrents", torrentId);
    }

    private void PublishPieceMapClearedSignalR(string infoHash)
    {
        PublishPieceMapUpdatedSignalR(infoHash, cleared: true);
    }

    private void PublishPieceMapUpdatedSignalR(string infoHash, bool cleared)
    {
        if (_signalRBroadcaster == null)
        {
            return;
        }

        PublishPieceSignalR(infoHash, new PieceMapUpdatedMessage(infoHash, cleared));
    }

    private int? ResolveTorrentId(string infoHash)
    {
        if (_torrentService == null || string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        try
        {
            var torrent = _torrentService.GetByInfoHash(infoHash);
            return torrent?.Id > 0 ? torrent.Id : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed class PendingBatch : IDisposable
    {
        public string InfoHash { get; }

        public List<int> PieceIndexes { get; } = new();

        public long BytesDownloaded { get; set; }

        public Timer Timer { get; set; }

        public object SyncLock { get; } = new();

        public bool IsDisposed { get; private set; }

        public PendingBatch(string infoHash)
        {
            InfoHash = infoHash;
        }

        public void Dispose()
        {
            lock (SyncLock)
            {
                IsDisposed = true;
                Timer?.Dispose();
                Timer = null;
                PieceIndexes.Clear();
            }
        }
    }
}
