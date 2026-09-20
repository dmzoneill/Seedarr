using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NzbDrone.Core.Torrents;

public class PieceCache : IPieceCache
{
    public const long DefaultMaxCacheSizeBytes = 64 * 1024 * 1024; // 64 MiB

    private readonly object _lock = new();
    private readonly Dictionary<(int TorrentId, int PieceIndex), LinkedListNode<CachedPiece>> _pieces = new();
    private readonly LinkedList<CachedPiece> _lruList = new();
    private readonly bool _synchronousFsync;
    private long _maxCacheSizeBytes;
    private long _currentCacheSizeBytes;

    public long MaxCacheSizeBytes
    {
        get
        {
            lock (_lock)
            {
                return _maxCacheSizeBytes;
            }
        }

        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Max cache size must be greater than zero.");
            }

            lock (_lock)
            {
                _maxCacheSizeBytes = value;
                EvictToLimit(0);
            }
        }
    }

    public long CurrentCacheSizeBytes
    {
        get
        {
            lock (_lock)
            {
                return _currentCacheSizeBytes;
            }
        }
    }

    public int CachedPieceCount
    {
        get
        {
            lock (_lock)
            {
                return _pieces.Count;
            }
        }
    }

    public PieceCache(long maxCacheSizeBytes = DefaultMaxCacheSizeBytes, bool synchronousFsync = true)
    {
        if (maxCacheSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCacheSizeBytes), "Max cache size must be greater than zero.");
        }

        _maxCacheSizeBytes = maxCacheSizeBytes;
        _synchronousFsync = synchronousFsync;
    }

    public bool AddBlock(int torrentId, int pieceIndex, int blockOffset, byte[] data, int pieceLength)
    {
        if (torrentId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(torrentId), "Torrent ID cannot be negative.");
        }

        if (pieceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceIndex), "Piece index cannot be negative.");
        }

        if (pieceLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceLength), "Piece length must be greater than zero.");
        }

        if (data == null)
        {
            throw new ArgumentNullException(nameof(data), "Block data cannot be null.");
        }

        if (blockOffset < 0 || blockOffset >= pieceLength)
        {
            throw new ArgumentOutOfRangeException(nameof(blockOffset), "Block offset must be within piece bounds.");
        }

        lock (_lock)
        {
            var key = (torrentId, pieceIndex);
            if (!_pieces.TryGetValue(key, out var node))
            {
                EvictToLimit(pieceLength);

                var newPiece = new CachedPiece(torrentId, pieceIndex, pieceLength);
                node = _lruList.AddFirst(newPiece);
                _pieces[key] = node;
                _currentCacheSizeBytes += pieceLength;
            }
            else
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }

            var piece = node.Value;
            var isComplete = piece.AddBlock(blockOffset, data);
            return isComplete;
        }
    }

    public bool VerifyAndFlushPiece(
        int torrentId,
        int pieceIndex,
        byte[] expectedHash,
        IMultiFilePieceStorage storage,
        Torrent torrent,
        IList<TorrentFile> files,
        string baseDirectory = null)
    {
        if (expectedHash == null || expectedHash.Length != 20)
        {
            return false;
        }

        if (storage == null)
        {
            return false;
        }

        CachedPiece piece;
        lock (_lock)
        {
            var key = (torrentId, pieceIndex);
            if (!_pieces.TryGetValue(key, out var node))
            {
                return false;
            }

            piece = node.Value;

            if (!piece.IsComplete)
            {
                return false;
            }

            _lruList.Remove(node);
            _lruList.AddFirst(node);
        }

        Span<byte> computedHash = stackalloc byte[20];
        if (!SHA1.TryHashData(piece.Buffer, computedHash, out var bytesWritten) || bytesWritten != 20)
        {
            DiscardPiece(torrentId, pieceIndex);
            return false;
        }

        var isMatch = CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
        if (!isMatch)
        {
            DiscardPiece(torrentId, pieceIndex);
            return false;
        }

        storage.WritePiece(torrent, files, pieceIndex, piece.Buffer, baseDirectory);

        if (_synchronousFsync)
        {
            PerformSynchronousFsync(storage, torrent, files, pieceIndex, baseDirectory);
        }

        lock (_lock)
        {
            piece.IsFlushed = true;
        }

        return true;
    }

    public bool IsPieceComplete(int torrentId, int pieceIndex)
    {
        lock (_lock)
        {
            return _pieces.TryGetValue((torrentId, pieceIndex), out var node) && node.Value.IsComplete;
        }
    }

    public bool IsPieceFlushed(int torrentId, int pieceIndex)
    {
        lock (_lock)
        {
            return _pieces.TryGetValue((torrentId, pieceIndex), out var node) && node.Value.IsFlushed;
        }
    }

    public bool ContainsPiece(int torrentId, int pieceIndex)
    {
        lock (_lock)
        {
            return _pieces.ContainsKey((torrentId, pieceIndex));
        }
    }

    public byte[] GetPieceData(int torrentId, int pieceIndex)
    {
        lock (_lock)
        {
            if (_pieces.TryGetValue((torrentId, pieceIndex), out var node))
            {
                var copy = new byte[node.Value.PieceLength];
                Buffer.BlockCopy(node.Value.Buffer, 0, copy, 0, node.Value.PieceLength);
                return copy;
            }

            return null;
        }
    }

    public bool TryGetPieceData(int torrentId, int pieceIndex, out byte[] data)
    {
        data = GetPieceData(torrentId, pieceIndex);
        return data != null;
    }

    public bool DiscardPiece(int torrentId, int pieceIndex)
    {
        lock (_lock)
        {
            var key = (torrentId, pieceIndex);
            if (_pieces.TryGetValue(key, out var node))
            {
                _pieces.Remove(key);
                _lruList.Remove(node);
                _currentCacheSizeBytes -= node.Value.PieceLength;
                return true;
            }

            return false;
        }
    }

    public void ClearTorrent(int torrentId)
    {
        lock (_lock)
        {
            var keysToRemove = _pieces.Keys.Where(k => k.TorrentId == torrentId).ToList();
            foreach (var key in keysToRemove)
            {
                if (_pieces.TryGetValue(key, out var node))
                {
                    _pieces.Remove(key);
                    _lruList.Remove(node);
                    _currentCacheSizeBytes -= node.Value.PieceLength;
                }
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pieces.Clear();
            _lruList.Clear();
            _currentCacheSizeBytes = 0;
        }
    }

    private void EvictToLimit(int incomingBytes)
    {
        while (_currentCacheSizeBytes + incomingBytes > _maxCacheSizeBytes && _lruList.Count > 0)
        {
            LinkedListNode<CachedPiece> targetNode = null;
            for (var curr = _lruList.Last; curr != null; curr = curr.Previous)
            {
                if (curr.Value.IsFlushed)
                {
                    targetNode = curr;
                    break;
                }
            }

            targetNode ??= _lruList.Last;

            if (targetNode == null)
            {
                break;
            }

            _lruList.Remove(targetNode);
            _pieces.Remove((targetNode.Value.TorrentId, targetNode.Value.PieceIndex));
            _currentCacheSizeBytes -= targetNode.Value.PieceLength;
        }
    }

    private static void PerformSynchronousFsync(
        IMultiFilePieceStorage storage,
        Torrent torrent,
        IList<TorrentFile> files,
        int pieceIndex,
        string baseDirectory)
    {
        if (storage is not MultiFilePieceStorage multiStorage ||
            multiStorage.HandlePool == null ||
            torrent == null ||
            files == null ||
            files.Count == 0)
        {
            return;
        }

        try
        {
            var pieceLength = torrent.PieceLength;
            if (pieceLength <= 0)
            {
                return;
            }

            var totalSize = torrent.TotalSize > 0
                ? torrent.TotalSize
                : files.Where(f => f != null && f.Size > 0).Sum(f => f.Size);

            if (totalSize <= 0)
            {
                return;
            }

            var resolver = new PieceBoundaryResolver();
            var slices = resolver.ResolvePiece(pieceIndex, pieceLength, totalSize, files);
            if (slices == null || slices.Count == 0)
            {
                return;
            }

            var flushedPaths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            foreach (var slice in slices)
            {
                var filePath = ResolveFilePath(baseDirectory, torrent, slice.File?.Path);
                if (string.IsNullOrWhiteSpace(filePath) || !flushedPaths.Add(filePath))
                {
                    continue;
                }

                try
                {
                    var handle = multiStorage.HandlePool.GetOrCreateHandle(filePath, writeAccess: true);
                    if (handle != null && !handle.IsClosed && !handle.IsInvalid)
                    {
                        RandomAccess.FlushToDisk(handle);
                    }
                }
                catch (Exception)
                {
                    // Best effort OS fsync
                }
            }
        }
        catch (Exception)
        {
            // Best effort OS fsync
        }
    }

    private static string ResolveFilePath(string baseDirectory, Torrent torrent, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (Path.IsPathRooted(filePath))
        {
            return filePath;
        }

        var basePath = !string.IsNullOrWhiteSpace(baseDirectory)
            ? baseDirectory
            : (!string.IsNullOrWhiteSpace(torrent?.SavePath) ? torrent.SavePath : string.Empty);

        return string.IsNullOrEmpty(basePath) ? filePath : Path.Combine(basePath, filePath);
    }

    private sealed class CachedPiece
    {
        public int TorrentId { get; }

        public int PieceIndex { get; }

        public int PieceLength { get; }

        public byte[] Buffer { get; }

        public List<(int Start, int End)> Ranges { get; } = new();

        public int TotalReceivedBytes { get; private set; }

        public bool IsComplete { get; internal set; }

        public bool IsFlushed { get; internal set; }

        public CachedPiece(int torrentId, int pieceIndex, int pieceLength)
        {
            TorrentId = torrentId;
            PieceIndex = pieceIndex;
            PieceLength = pieceLength;
            Buffer = new byte[pieceLength];
        }

        public bool AddBlock(int blockOffset, byte[] data)
        {
            var copyLength = Math.Min(data.Length, PieceLength - blockOffset);
            if (copyLength <= 0)
            {
                return IsComplete;
            }

            System.Buffer.BlockCopy(data, 0, Buffer, blockOffset, copyLength);

            AddRange(blockOffset, blockOffset + copyLength);
            if (TotalReceivedBytes >= PieceLength && Ranges.Count == 1 && Ranges[0].Start == 0 && Ranges[0].End == PieceLength)
            {
                IsComplete = true;
            }

            return IsComplete;
        }

        private void AddRange(int start, int end)
        {
            var newRange = (Start: start, End: end);
            var merged = false;
            var i = 0;

            while (i < Ranges.Count)
            {
                var r = Ranges[i];
                if (newRange.Start <= r.End && newRange.End >= r.Start)
                {
                    newRange = (Math.Min(newRange.Start, r.Start), Math.Max(newRange.End, r.End));
                    Ranges.RemoveAt(i);
                }
                else if (r.Start > newRange.End)
                {
                    Ranges.Insert(i, newRange);
                    merged = true;
                    break;
                }
                else
                {
                    i++;
                }
            }

            if (!merged && !Ranges.Contains(newRange))
            {
                Ranges.Add(newRange);
            }

            var total = 0;
            foreach (var range in Ranges)
            {
                total += range.End - range.Start;
            }

            TotalReceivedBytes = total;
        }
    }
}
