using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Peers;

/// <summary>
/// Maintains swarm-wide piece availability and indexed rarity buckets for O(1) rarest-first selection.
/// </summary>
public class SwarmPieceHistogram
{
    private readonly ushort[] _pieceAvailability;
    private readonly List<HashSet<int>> _rarityBuckets;
    private readonly object _syncLock = new();

    public int TotalPieces { get; }

    public SwarmPieceHistogram(int pieceCount)
    {
        if (pieceCount < 0)
        {
            pieceCount = 0;
        }

        TotalPieces = pieceCount;
        _pieceAvailability = new ushort[pieceCount];
        _rarityBuckets = new List<HashSet<int>>
        {
            new(Enumerable.Range(0, pieceCount))
        };
    }

    /// <summary>
    /// Gets the current availability frequency for a specific piece across the swarm.
    /// </summary>
    public int GetAvailability(int pieceIndex)
    {
        lock (_syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= _pieceAvailability.Length)
            {
                return 0;
            }

            return _pieceAvailability[pieceIndex];
        }
    }

    /// <summary>
    /// Increments availability for a single piece and moves it to the next rarity bucket in O(1).
    /// </summary>
    public void IncrementPiece(int pieceIndex)
    {
        lock (_syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= _pieceAvailability.Length)
            {
                return;
            }

            var currentFreq = _pieceAvailability[pieceIndex];
            if (currentFreq == ushort.MaxValue)
            {
                return;
            }

            if (currentFreq < _rarityBuckets.Count)
            {
                _rarityBuckets[currentFreq].Remove(pieceIndex);
            }

            var nextFreq = ++_pieceAvailability[pieceIndex];
            while (_rarityBuckets.Count <= nextFreq)
            {
                _rarityBuckets.Add(new HashSet<int>());
            }

            _rarityBuckets[nextFreq].Add(pieceIndex);
        }
    }

    /// <summary>
    /// Decrements availability for a single piece and moves it to the previous rarity bucket in O(1).
    /// </summary>
    public void DecrementPiece(int pieceIndex)
    {
        lock (_syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= _pieceAvailability.Length)
            {
                return;
            }

            var currentFreq = _pieceAvailability[pieceIndex];
            if (currentFreq == 0)
            {
                return;
            }

            if (currentFreq < _rarityBuckets.Count)
            {
                _rarityBuckets[currentFreq].Remove(pieceIndex);
            }

            var nextFreq = --_pieceAvailability[pieceIndex];
            _rarityBuckets[nextFreq].Add(pieceIndex);
        }
    }

    /// <summary>
    /// Bulk increments piece availability for all pieces held by a peer.
    /// </summary>
    public void RegisterPeer(bool[] peerPieces)
    {
        if (peerPieces == null)
        {
            return;
        }

        lock (_syncLock)
        {
            var len = Math.Min(peerPieces.Length, _pieceAvailability.Length);
            for (var i = 0; i < len; i++)
            {
                if (!peerPieces[i])
                {
                    continue;
                }

                var currentFreq = _pieceAvailability[i];
                if (currentFreq == ushort.MaxValue)
                {
                    continue;
                }

                if (currentFreq < _rarityBuckets.Count)
                {
                    _rarityBuckets[currentFreq].Remove(i);
                }

                var nextFreq = ++_pieceAvailability[i];
                while (_rarityBuckets.Count <= nextFreq)
                {
                    _rarityBuckets.Add(new HashSet<int>());
                }

                _rarityBuckets[nextFreq].Add(i);
            }
        }
    }

    /// <summary>
    /// Bulk increments piece availability for all pieces held by a peer represented by a BitArray.
    /// </summary>
    public void RegisterPeer(BitArray peerPieces)
    {
        if (peerPieces == null)
        {
            return;
        }

        lock (_syncLock)
        {
            var len = Math.Min(peerPieces.Length, _pieceAvailability.Length);
            for (var i = 0; i < len; i++)
            {
                if (!peerPieces[i])
                {
                    continue;
                }

                var currentFreq = _pieceAvailability[i];
                if (currentFreq == ushort.MaxValue)
                {
                    continue;
                }

                if (currentFreq < _rarityBuckets.Count)
                {
                    _rarityBuckets[currentFreq].Remove(i);
                }

                var nextFreq = ++_pieceAvailability[i];
                while (_rarityBuckets.Count <= nextFreq)
                {
                    _rarityBuckets.Add(new HashSet<int>());
                }

                _rarityBuckets[nextFreq].Add(i);
            }
        }
    }

    /// <summary>
    /// Bulk decrements piece availability for all pieces held by a disconnecting peer.
    /// </summary>
    public void UnregisterPeer(bool[] peerPieces)
    {
        if (peerPieces == null)
        {
            return;
        }

        lock (_syncLock)
        {
            var len = Math.Min(peerPieces.Length, _pieceAvailability.Length);
            for (var i = 0; i < len; i++)
            {
                if (!peerPieces[i])
                {
                    continue;
                }

                var currentFreq = _pieceAvailability[i];
                if (currentFreq == 0)
                {
                    continue;
                }

                if (currentFreq < _rarityBuckets.Count)
                {
                    _rarityBuckets[currentFreq].Remove(i);
                }

                var nextFreq = --_pieceAvailability[i];
                _rarityBuckets[nextFreq].Add(i);
            }
        }
    }

    /// <summary>
    /// Bulk decrements piece availability for all pieces held by a disconnecting peer represented by a BitArray.
    /// </summary>
    public void UnregisterPeer(BitArray peerPieces)
    {
        if (peerPieces == null)
        {
            return;
        }

        lock (_syncLock)
        {
            var len = Math.Min(peerPieces.Length, _pieceAvailability.Length);
            for (var i = 0; i < len; i++)
            {
                if (!peerPieces[i])
                {
                    continue;
                }

                var currentFreq = _pieceAvailability[i];
                if (currentFreq == 0)
                {
                    continue;
                }

                if (currentFreq < _rarityBuckets.Count)
                {
                    _rarityBuckets[currentFreq].Remove(i);
                }

                var nextFreq = --_pieceAvailability[i];
                _rarityBuckets[nextFreq].Add(i);
            }
        }
    }

    /// <summary>
    /// Selects the rarest pieces available across the swarm matching local missing and peer availability,
    /// searching buckets from lowest non-zero availability upward in O(1) amortized time.
    /// </summary>
    public List<int> GetRarestPieces(bool[] localMissing, bool[] peerPieces, int maxCount)
    {
        if (maxCount <= 0 || _pieceAvailability.Length == 0)
        {
            return new List<int>();
        }

        lock (_syncLock)
        {
            var result = new List<int>(Math.Min(maxCount, _pieceAvailability.Length));
            for (var k = 1; k < _rarityBuckets.Count; k++)
            {
                var bucket = _rarityBuckets[k];
                if (bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                foreach (var pieceIndex in bucket)
                {
                    if (localMissing != null && (pieceIndex >= localMissing.Length || !localMissing[pieceIndex]))
                    {
                        continue;
                    }

                    if (peerPieces != null && (pieceIndex >= peerPieces.Length || !peerPieces[pieceIndex]))
                    {
                        continue;
                    }

                    result.Add(pieceIndex);
                    if (result.Count >= maxCount)
                    {
                        return result;
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Selects the rarest pieces available across the swarm matching local missing and peer availability using BitArrays.
    /// </summary>
    public List<int> GetRarestPieces(BitArray localMissing, BitArray peerPieces, int maxCount)
    {
        if (maxCount <= 0 || _pieceAvailability.Length == 0)
        {
            return new List<int>();
        }

        lock (_syncLock)
        {
            var result = new List<int>(Math.Min(maxCount, _pieceAvailability.Length));
            for (var k = 1; k < _rarityBuckets.Count; k++)
            {
                var bucket = _rarityBuckets[k];
                if (bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                foreach (var pieceIndex in bucket)
                {
                    if (localMissing != null && (pieceIndex >= localMissing.Length || !localMissing[pieceIndex]))
                    {
                        continue;
                    }

                    if (peerPieces != null && (pieceIndex >= peerPieces.Length || !peerPieces[pieceIndex]))
                    {
                        continue;
                    }

                    result.Add(pieceIndex);
                    if (result.Count >= maxCount)
                    {
                        return result;
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Gets all piece indices currently in a specific rarity bucket.
    /// </summary>
    public IReadOnlySet<int> GetPiecesInBucket(int availability)
    {
        lock (_syncLock)
        {
            if (availability < 0 || availability >= _rarityBuckets.Count)
            {
                return new HashSet<int>();
            }

            return new HashSet<int>(_rarityBuckets[availability]);
        }
    }

    /// <summary>
    /// Gets the count of pieces currently in a specific rarity bucket.
    /// </summary>
    public int GetBucketPieceCount(int availability)
    {
        lock (_syncLock)
        {
            if (availability < 0 || availability >= _rarityBuckets.Count)
            {
                return 0;
            }

            return _rarityBuckets[availability].Count;
        }
    }
}
