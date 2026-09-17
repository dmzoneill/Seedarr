using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers.PiecePicker;

public class PieceBlock
{
    public int PieceIndex { get; }
    public int Begin { get; }
    public int Length { get; }
    public bool IsRequested { get; set; }
    public bool IsCompleted { get; set; }
    public PeerConnection RequestedFrom { get; set; }
    public DateTime? RequestedAt { get; set; }
    public CancellationTokenSource TimeoutCts { get; set; }

    public PieceBlock(int pieceIndex, int begin, int length)
    {
        PieceIndex = pieceIndex;
        Begin = begin;
        Length = length;
    }
}

public class ActivePiece
{
    private readonly object _lock = new();

    public int PieceIndex { get; }
    public int TotalLength { get; }
    public int BlockSize { get; }
    public List<PieceBlock> Blocks { get; } = new();

    public ActivePiece(int pieceIndex, int totalLength, int blockSize = 16384)
    {
        PieceIndex = pieceIndex;
        TotalLength = totalLength;
        BlockSize = blockSize;

        var offset = 0;
        while (offset < totalLength)
        {
            var len = Math.Min(blockSize, totalLength - offset);
            Blocks.Add(new PieceBlock(pieceIndex, offset, len));
            offset += len;
        }
    }

    public PieceBlock GetBlock(int begin)
    {
        lock (_lock)
        {
            return Blocks.FirstOrDefault(b => b.Begin == begin);
        }
    }

    public PieceBlock GetNextAvailableBlock()
    {
        lock (_lock)
        {
            return Blocks.FirstOrDefault(b => !b.IsRequested && !b.IsCompleted);
        }
    }
}

public interface IPieceBlockDownloader
{
    void OnBlockRejected(PeerConnection peer, int pieceIndex, int begin, int length);
}

public interface IDownloadManager : IPieceBlockDownloader
{
    TimeSpan RequestTimeout { get; set; }
    IReadOnlyDictionary<int, ActivePiece> ActivePieces { get; }

    ActivePiece AddActivePiece(int pieceIndex, int pieceLength, int blockSize = 16384);
    ActivePiece GetActivePiece(int pieceIndex);
    bool RemoveActivePiece(int pieceIndex);

    bool CanRequestBlock(PeerConnection peer, int pieceIndex);
    PieceBlock RequestBlock(PeerConnection peer, int pieceIndex = -1);
    PieceBlock RequestBlock(PeerConnection peer, int pieceIndex, bool sequential);
    IPiecePicker SequentialPicker { get; }
    IPiecePicker RarestFirstPicker { get; }
    IPiecePicker GetPicker(Torrent torrent);
    IPiecePicker GetPicker(bool sequential);
    void MarkBlockRequested(PeerConnection peer, int pieceIndex, int begin, int length, TimeSpan? timeout = null);
    void MarkBlockCompleted(int pieceIndex, int begin, int length);

    bool IsBlockPending(int pieceIndex, int begin);
    IReadOnlyList<PieceBlock> GetPendingBlocks(PeerConnection peer = null);
    void ProcessTimeouts();
}

public class PiecePicker : IDownloadManager, IPiecePicker
{
    private readonly ConcurrentDictionary<int, ActivePiece> _activePieces = new();
    private readonly object _syncLock = new();
    private readonly IPiecePicker _sequentialPicker;
    private readonly IPiecePicker _rarestFirstPicker;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public IReadOnlyDictionary<int, ActivePiece> ActivePieces => _activePieces;
    public IPiecePicker SequentialPicker => _sequentialPicker;
    public IPiecePicker RarestFirstPicker => _rarestFirstPicker;

    public PiecePicker(IPiecePicker sequentialPicker = null, IPiecePicker rarestFirstPicker = null)
    {
        _rarestFirstPicker = rarestFirstPicker ?? new RarestFirstPiecePicker();
        _sequentialPicker = sequentialPicker ?? new SequentialPiecePicker(_rarestFirstPicker);
    }

    public IPiecePicker GetPicker(Torrent torrent)
    {
        return torrent?.SequentialDownload == true ? _sequentialPicker : _rarestFirstPicker;
    }

    public IPiecePicker GetPicker(bool sequential)
    {
        return sequential ? _sequentialPicker : _rarestFirstPicker;
    }

    public int? PickPiece(
        BitArray myPieces,
        BitArray peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow = 20,
        double rarestFirstRatio = 0.2)
    {
        return GetPicker(sequential).PickPiece(myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio);
    }

    public int? PickPiece(
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow = 20,
        double rarestFirstRatio = 0.2)
    {
        return GetPicker(sequential).PickPiece(myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio);
    }

    public int? PickPiece(
        BitArray myPieces,
        BitArray peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow,
        double rarestFirstRatio,
        IReadOnlyCollection<int> activePieces)
    {
        return GetPicker(sequential).PickPiece(myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, activePieces);
    }

    public int? PickPiece(
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow,
        double rarestFirstRatio,
        IReadOnlyCollection<int> activePieces)
    {
        return GetPicker(sequential).PickPiece(myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, activePieces);
    }

    public ActivePiece AddActivePiece(int pieceIndex, int pieceLength, int blockSize = 16384)
    {
        var piece = new ActivePiece(pieceIndex, pieceLength, blockSize);
        _activePieces[pieceIndex] = piece;
        return piece;
    }

    public ActivePiece GetActivePiece(int pieceIndex)
    {
        _activePieces.TryGetValue(pieceIndex, out var piece);
        return piece;
    }

    public bool RemoveActivePiece(int pieceIndex)
    {
        return _activePieces.TryRemove(pieceIndex, out _);
    }

    public bool CanRequestBlock(PeerConnection peer, int pieceIndex)
    {
        if (peer == null)
        {
            return false;
        }

        if (peer.PendingRequestCount >= peer.MaxPipelinedRequests)
        {
            return false;
        }

        return !peer.PeerChoking || (peer.SupportsFastExtension && peer.RemoteAllowedFastPieces.Contains(pieceIndex));
    }

    public PieceBlock RequestBlock(PeerConnection peer, int pieceIndex = -1)
    {
        return RequestBlock(peer, pieceIndex, false);
    }

    public PieceBlock RequestBlock(PeerConnection peer, int pieceIndex, Torrent torrent)
    {
        return RequestBlock(peer, pieceIndex, torrent?.SequentialDownload ?? false);
    }

    public PieceBlock RequestBlock(PeerConnection peer, int pieceIndex, bool sequential)
    {
        if (peer == null)
        {
            return null;
        }

        if (peer.PendingRequestCount >= peer.MaxPipelinedRequests)
        {
            return null;
        }

        lock (_syncLock)
        {
            if (pieceIndex >= 0)
            {
                if (!_activePieces.TryGetValue(pieceIndex, out var activePiece))
                {
                    return null;
                }

                if (!CanPeerServePiece(peer, pieceIndex))
                {
                    return null;
                }

                var block = activePiece.GetNextAvailableBlock();
                if (block != null)
                {
                    AssignBlock(peer, block);
                    return block;
                }

                return null;
            }

            // Prioritize bootstrap block requests from RemoteAllowedFastPieces when starting connection while choked
            if (peer.SupportsFastExtension && peer.RemoteAllowedFastPieces.Count > 0)
            {
                foreach (var allowedIndex in peer.RemoteAllowedFastPieces)
                {
                    if (_activePieces.TryGetValue(allowedIndex, out var allowedPiece) && CanPeerServePiece(peer, allowedIndex))
                    {
                        var block = allowedPiece.GetNextAvailableBlock();
                        if (block != null)
                        {
                            AssignBlock(peer, block);
                            return block;
                        }
                    }
                }
            }

            if (peer.PeerChoking)
            {
                return null;
            }

            var piecesToConsider = sequential
                ? (IEnumerable<KeyValuePair<int, ActivePiece>>)_activePieces.OrderBy(kvp => kvp.Key)
                : _activePieces;

            foreach (var kvp in piecesToConsider)
            {
                var idx = kvp.Key;
                var activePiece = kvp.Value;

                if (!CanPeerServePiece(peer, idx))
                {
                    continue;
                }

                var block = activePiece.GetNextAvailableBlock();
                if (block != null)
                {
                    AssignBlock(peer, block);
                    return block;
                }
            }

            return null;
        }
    }

    public void MarkBlockRequested(PeerConnection peer, int pieceIndex, int begin, int length, TimeSpan? timeout = null)
    {
        lock (_syncLock)
        {
            if (_activePieces.TryGetValue(pieceIndex, out var activePiece))
            {
                var block = activePiece.GetBlock(begin);
                if (block != null)
                {
                    AssignBlock(peer, block);
                }
            }
        }
    }

    public void OnBlockRejected(PeerConnection peer, int pieceIndex, int begin, int length)
    {
        lock (_syncLock)
        {
            if (_activePieces.TryGetValue(pieceIndex, out var activePiece))
            {
                var block = activePiece.GetBlock(begin);
                if (block != null && block.IsRequested)
                {
                    block.IsRequested = false;
                    block.RequestedFrom = null;
                    block.RequestedAt = null;

                    if (block.TimeoutCts != null)
                    {
                        try
                        {
                            block.TimeoutCts.Cancel();
                            block.TimeoutCts.Dispose();
                        }
                        catch
                        {
                        }

                        block.TimeoutCts = null;
                    }
                }
            }
        }
    }

    public void MarkBlockCompleted(int pieceIndex, int begin, int length)
    {
        lock (_syncLock)
        {
            if (_activePieces.TryGetValue(pieceIndex, out var activePiece))
            {
                var block = activePiece.GetBlock(begin);
                if (block != null)
                {
                    block.IsCompleted = true;
                    block.IsRequested = false;
                    block.RequestedFrom = null;
                    block.RequestedAt = null;

                    if (block.TimeoutCts != null)
                    {
                        try
                        {
                            block.TimeoutCts.Cancel();
                            block.TimeoutCts.Dispose();
                        }
                        catch
                        {
                        }

                        block.TimeoutCts = null;
                    }
                }
            }
        }
    }

    public bool IsBlockPending(int pieceIndex, int begin)
    {
        if (_activePieces.TryGetValue(pieceIndex, out var activePiece))
        {
            var block = activePiece.GetBlock(begin);
            return block != null && block.IsRequested && !block.IsCompleted;
        }

        return false;
    }

    public IReadOnlyList<PieceBlock> GetPendingBlocks(PeerConnection peer = null)
    {
        var result = new List<PieceBlock>();
        foreach (var piece in _activePieces.Values)
        {
            foreach (var block in piece.Blocks)
            {
                if (block.IsRequested && !block.IsCompleted)
                {
                    if (peer == null || block.RequestedFrom == peer)
                    {
                        result.Add(block);
                    }
                }
            }
        }

        return result;
    }

    public void ProcessTimeouts()
    {
        var now = DateTime.UtcNow;
        lock (_syncLock)
        {
            foreach (var piece in _activePieces.Values)
            {
                foreach (var block in piece.Blocks)
                {
                    if (block.IsRequested && block.RequestedAt.HasValue && now - block.RequestedAt.Value > RequestTimeout)
                    {
                        var peer = block.RequestedFrom;
                        block.IsRequested = false;
                        block.RequestedFrom = null;
                        block.RequestedAt = null;

                        if (block.TimeoutCts != null)
                        {
                            try
                            {
                                block.TimeoutCts.Cancel();
                                block.TimeoutCts.Dispose();
                            }
                            catch
                            {
                            }

                            block.TimeoutCts = null;
                        }

                        if (peer != null && peer.PendingRequestCount > 0)
                        {
                            peer.PendingRequestCount--;
                        }
                    }
                }
            }
        }
    }

    private static bool CanPeerServePiece(PeerConnection peer, int pieceIndex)
    {
        var canRequest = !peer.PeerChoking || (peer.SupportsFastExtension && peer.RemoteAllowedFastPieces.Contains(pieceIndex));
        if (!canRequest)
        {
            return false;
        }

        if (peer.SupportsFastExtension && peer.RemoteAllowedFastPieces.Contains(pieceIndex))
        {
            return true;
        }

        if (peer.IsSeed)
        {
            return true;
        }

        if (peer.PeerPieces == null || peer.PeerPieces.Length <= pieceIndex)
        {
            return false;
        }

        return peer.PeerPieces[pieceIndex];
    }

    private static void AssignBlock(PeerConnection peer, PieceBlock block)
    {
        block.IsRequested = true;
        block.RequestedFrom = peer;
        block.RequestedAt = DateTime.UtcNow;

        if (block.TimeoutCts != null)
        {
            try
            {
                block.TimeoutCts.Cancel();
                block.TimeoutCts.Dispose();
            }
            catch
            {
            }
        }

        block.TimeoutCts = new CancellationTokenSource();
        peer.PendingRequestCount++;
    }
}
