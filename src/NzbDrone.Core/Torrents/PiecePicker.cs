using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public class PiecePicker : IPiecePicker
{
    private readonly ConcurrentDictionary<string, HashSet<int>> _activePieces = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public HashSet<int> GetActivePieces(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return new HashSet<int>();
        }

        lock (_lock)
        {
            if (_activePieces.TryGetValue(infoHash, out var set))
            {
                return new HashSet<int>(set);
            }

            return new HashSet<int>();
        }
    }

    public bool IsPieceActive(string infoHash, int pieceIndex)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return false;
        }

        lock (_lock)
        {
            return _activePieces.TryGetValue(infoHash, out var set) && set.Contains(pieceIndex);
        }
    }

    public void MarkPieceActive(string infoHash, int pieceIndex)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        lock (_lock)
        {
            if (!_activePieces.TryGetValue(infoHash, out var set))
            {
                set = new HashSet<int>();
                _activePieces[infoHash] = set;
            }

            set.Add(pieceIndex);
        }
    }

    public void MarkPieceInactive(string infoHash, int pieceIndex)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        lock (_lock)
        {
            if (_activePieces.TryGetValue(infoHash, out var set))
            {
                set.Remove(pieceIndex);
            }
        }
    }

    public void Clear(string infoHash)
    {
        if (string.IsNullOrEmpty(infoHash))
        {
            return;
        }

        lock (_lock)
        {
            _activePieces.TryRemove(infoHash, out _);
        }
    }

    public int? PickNextPiece(string infoHash, int pieceCount, bool[] verifiedPieces, HashSet<int> activePieces = null)
    {
        var active = activePieces ?? GetActivePieces(infoHash);
        for (var i = 0; i < pieceCount; i++)
        {
            var isVerified = verifiedPieces != null && i < verifiedPieces.Length && verifiedPieces[i];
            if (!isVerified && !active.Contains(i))
            {
                return i;
            }
        }

        return null;
    }
}
