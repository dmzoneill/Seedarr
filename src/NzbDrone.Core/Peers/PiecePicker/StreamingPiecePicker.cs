using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Peers.PiecePicker;

public class StreamingPiecePicker : IPiecePicker
{
    public const int DefaultUrgentWindowSize = 5;
    public const int DefaultLookaheadWindowSize = 25;
    public const int DefaultTailWindowSize = 2;

    private readonly ConcurrentDictionary<int, int> _playbackPositions = new();
    private readonly IPiecePicker _fallbackPicker;
    private readonly IRandomNumberGenerator _random;
    private int? _lastActiveTorrentId;

    public int UrgentWindowSize { get; set; } = DefaultUrgentWindowSize;
    public int LookaheadWindowSize { get; set; } = DefaultLookaheadWindowSize;
    public int TailWindowSize { get; set; } = DefaultTailWindowSize;

    public event Action<int, int> PlayheadMoved;

    public StreamingPiecePicker(
        IRandomNumberGenerator random = null,
        int urgentWindowSize = DefaultUrgentWindowSize,
        int lookaheadWindowSize = DefaultLookaheadWindowSize,
        int tailWindowSize = DefaultTailWindowSize)
    {
        _random = random ?? new RandomNumberGenerator();
        _fallbackPicker = new SequentialPiecePicker(new RarestFirstPiecePicker(_random), _random);
        UrgentWindowSize = urgentWindowSize > 0 ? urgentWindowSize : DefaultUrgentWindowSize;
        LookaheadWindowSize = lookaheadWindowSize > 0 ? lookaheadWindowSize : DefaultLookaheadWindowSize;
        TailWindowSize = tailWindowSize > 0 ? tailWindowSize : DefaultTailWindowSize;
    }

    public void SetPlaybackPosition(int torrentId, long byteOffset, long pieceLength)
    {
        var headPiece = pieceLength > 0 ? (int)(byteOffset / pieceLength) : 0;
        SetHeadPiece(torrentId, headPiece);
    }

    public void SetPlaybackPosition(long byteOffset, long pieceLength)
    {
        SetPlaybackPosition(0, byteOffset, pieceLength);
    }

    public void SetHeadPiece(int torrentId, int headPiece)
    {
        var normalizedHead = Math.Max(0, headPiece);
        _playbackPositions[torrentId] = normalizedHead;
        _lastActiveTorrentId = torrentId;
        PlayheadMoved?.Invoke(torrentId, normalizedHead);
    }

    public void SetHeadPiece(int headPiece)
    {
        SetHeadPiece(0, headPiece);
    }

    public int? GetHeadPiece(int torrentId = 0)
    {
        if (_playbackPositions.TryGetValue(torrentId, out var head))
        {
            return head;
        }

        if (_lastActiveTorrentId.HasValue && _playbackPositions.TryGetValue(_lastActiveTorrentId.Value, out head))
        {
            return head;
        }

        return null;
    }

    public bool HasActiveStream(int torrentId)
    {
        return _playbackPositions.ContainsKey(torrentId);
    }

    public bool HasAnyActiveStream()
    {
        return !_playbackPositions.IsEmpty;
    }

    public void ClearPlaybackPosition(int torrentId)
    {
        _playbackPositions.TryRemove(torrentId, out _);
        if (_lastActiveTorrentId == torrentId)
        {
            _lastActiveTorrentId = null;
        }
    }

    public void ClearAll()
    {
        _playbackPositions.Clear();
        _lastActiveTorrentId = null;
    }

    public IReadOnlyList<int> GetUrgentPieces(int torrentId, int pieceCount)
    {
        var head = GetHeadPiece(torrentId) ?? 0;
        head = Math.Clamp(head, 0, Math.Max(0, pieceCount - 1));
        var count = Math.Min(UrgentWindowSize, Math.Max(0, pieceCount - head));
        return count > 0 ? Enumerable.Range(head, count).ToList() : Array.Empty<int>();
    }

    public IReadOnlyList<int> GetLookaheadPieces(int torrentId, int pieceCount)
    {
        var head = GetHeadPiece(torrentId) ?? 0;
        head = Math.Clamp(head, 0, Math.Max(0, pieceCount - 1));
        var start = Math.Min(head + UrgentWindowSize, pieceCount);
        var end = Math.Min(head + LookaheadWindowSize, pieceCount);
        var count = Math.Max(0, end - start);
        return count > 0 ? Enumerable.Range(start, count).ToList() : Array.Empty<int>();
    }

    public IReadOnlyList<int> GetTailPieces(int pieceCount, IReadOnlyCollection<int> customBoundaryPieces = null)
    {
        if (pieceCount <= 0)
        {
            return Array.Empty<int>();
        }

        if (customBoundaryPieces != null && customBoundaryPieces.Count > 0)
        {
            return customBoundaryPieces.Where(p => p >= Math.Max(0, pieceCount - TailWindowSize) && p < pieceCount).ToList();
        }

        var start = Math.Max(0, pieceCount - TailWindowSize);
        var count = pieceCount - start;
        return Enumerable.Range(start, count).ToList();
    }

    public int? PickPiece(
        BitArray myPieces,
        BitArray peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow = 20,
        double rarestFirstRatio = 0.2)
    {
        var list = peerPieces != null ? new[] { peerPieces } : Array.Empty<BitArray>();
        return PickPiece(null, myPieces, list, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, null, false, null);
    }

    public int? PickPiece(
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow = 20,
        double rarestFirstRatio = 0.2)
    {
        return PickPiece(null, myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, null, false, null);
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
        var list = peerPieces != null ? new[] { peerPieces } : Array.Empty<BitArray>();
        return PickPiece(null, myPieces, list, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, activePieces, false, null);
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
        return PickPiece(null, myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, activePieces, false, null);
    }

    public int? PickPiece(
        BitArray myPieces,
        BitArray peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow,
        double rarestFirstRatio,
        IReadOnlyCollection<int> activePieces,
        bool firstLastPiecePrio,
        IReadOnlyCollection<int> customBoundaryPieces = null)
    {
        var list = peerPieces != null ? new[] { peerPieces } : Array.Empty<BitArray>();
        return PickPiece(null, myPieces, list, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, activePieces, firstLastPiecePrio, customBoundaryPieces);
    }

    public int? PickPiece(
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow,
        double rarestFirstRatio,
        IReadOnlyCollection<int> activePieces,
        bool firstLastPiecePrio,
        IReadOnlyCollection<int> customBoundaryPieces = null)
    {
        return PickPiece(null, myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, activePieces, firstLastPiecePrio, customBoundaryPieces);
    }

    public int? PickPiece(
        int torrentId,
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow = 20,
        double rarestFirstRatio = 0.2,
        IReadOnlyCollection<int> activePieces = null,
        bool firstLastPiecePrio = false,
        IReadOnlyCollection<int> customBoundaryPieces = null)
    {
        return PickPiece((int?)torrentId, myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, activePieces, firstLastPiecePrio, customBoundaryPieces);
    }

    public int? PickPiece(
        int? torrentId,
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow,
        double rarestFirstRatio,
        IReadOnlyCollection<int> activePieces,
        bool firstLastPiecePrio,
        IReadOnlyCollection<int> customBoundaryPieces = null)
    {
        if (myPieces == null || myPieces.Length == 0 || peerPieces == null || peerPieces.Count == 0)
        {
            return null;
        }

        var pieceCount = myPieces.Length;
        int? resolvedHead = null;

        if (torrentId.HasValue && _playbackPositions.TryGetValue(torrentId.Value, out var th))
        {
            resolvedHead = th;
        }
        else if (_lastActiveTorrentId.HasValue && _playbackPositions.TryGetValue(_lastActiveTorrentId.Value, out var lh))
        {
            resolvedHead = lh;
        }
        else if (_playbackPositions.TryGetValue(0, out var zh))
        {
            resolvedHead = zh;
        }
        else if (_playbackPositions.Any())
        {
            resolvedHead = _playbackPositions.Values.First();
        }

        if (!resolvedHead.HasValue)
        {
            return _fallbackPicker.PickPiece(myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, activePieces, firstLastPiecePrio, customBoundaryPieces);
        }

        var head = Math.Clamp(resolvedHead.Value, 0, Math.Max(0, pieceCount - 1));

        // 1. Urgent window: [head, head + UrgentWindowSize)
        var urgentEnd = Math.Min(head + UrgentWindowSize, pieceCount);
        var urgentCandidates = new List<int>();
        for (var i = head; i < urgentEnd; i++)
        {
            if (!myPieces[i] && PeerHasPiece(peerPieces, i))
            {
                urgentCandidates.Add(i);
            }
        }

        var urgentNonActive = activePieces != null && activePieces.Count > 0
            ? urgentCandidates.Where(c => !activePieces.Contains(c)).ToList()
            : urgentCandidates;

        if (urgentNonActive.Count > 0)
        {
            return urgentNonActive.OrderBy(c => c).First();
        }

        // Tail boundary pieces
        var tailIndices = GetTailPieces(pieceCount, customBoundaryPieces);
        var tailCandidates = new List<int>();
        foreach (var t in tailIndices)
        {
            if (t >= 0 && t < pieceCount && !myPieces[t] && PeerHasPiece(peerPieces, t))
            {
                tailCandidates.Add(t);
            }
        }

        var tailNonActive = activePieces != null && activePieces.Count > 0
            ? tailCandidates.Where(c => !activePieces.Contains(c)).ToList()
            : tailCandidates;

        // If firstLastPiecePrio is true and urgent window is satisfied, prioritize tail metadata boundary
        if (firstLastPiecePrio && tailNonActive.Count > 0)
        {
            return tailNonActive.First();
        }

        // 2. Lookahead buffer window: [urgentEnd, head + LookaheadWindowSize)
        var lookaheadLength = lookaheadWindow > 0 ? lookaheadWindow : LookaheadWindowSize;
        var lookaheadEnd = Math.Min(head + lookaheadLength, pieceCount);
        var lookaheadCandidates = new List<int>();
        for (var i = urgentEnd; i < lookaheadEnd; i++)
        {
            if (!myPieces[i] && PeerHasPiece(peerPieces, i))
            {
                lookaheadCandidates.Add(i);
            }
        }

        var lookaheadNonActive = activePieces != null && activePieces.Count > 0
            ? lookaheadCandidates.Where(c => !activePieces.Contains(c)).ToList()
            : lookaheadCandidates;

        if (lookaheadNonActive.Count > 0)
        {
            return lookaheadNonActive.OrderBy(c => c).First();
        }

        // 3. Tail boundary if not already picked
        if (tailNonActive.Count > 0)
        {
            return tailNonActive.First();
        }

        // 4. Background pieces (all other missing pieces outside urgent, lookahead, tail)
        var urgentSet = new HashSet<int>(Enumerable.Range(head, Math.Max(0, urgentEnd - head)));
        var lookaheadSet = new HashSet<int>(Enumerable.Range(urgentEnd, Math.Max(0, lookaheadEnd - urgentEnd)));
        var tailSet = new HashSet<int>(tailIndices);

        var backgroundCandidates = new List<int>();
        for (var i = 0; i < pieceCount; i++)
        {
            if (urgentSet.Contains(i) || lookaheadSet.Contains(i) || tailSet.Contains(i))
            {
                continue;
            }

            if (!myPieces[i] && PeerHasPiece(peerPieces, i))
            {
                backgroundCandidates.Add(i);
            }
        }

        var backgroundNonActive = activePieces != null && activePieces.Count > 0
            ? backgroundCandidates.Where(c => !activePieces.Contains(c)).ToList()
            : backgroundCandidates;

        if (backgroundNonActive.Count > 0)
        {
            if (rarestFirstRatio > 0 && pieceAvailability != null)
            {
                return PickRarestFromList(backgroundNonActive, pieceAvailability, peerPieces);
            }

            return backgroundNonActive.OrderBy(c => c).First();
        }

        // 5. Endgame mode: duplicate stalled requests for active candidates
        if (urgentCandidates.Count > 0)
        {
            return urgentCandidates.OrderBy(c => c).First();
        }

        if (lookaheadCandidates.Count > 0)
        {
            return lookaheadCandidates.OrderBy(c => c).First();
        }

        if (tailCandidates.Count > 0)
        {
            return tailCandidates.First();
        }

        if (backgroundCandidates.Count > 0)
        {
            if (rarestFirstRatio > 0 && pieceAvailability != null)
            {
                return PickRarestFromList(backgroundCandidates, pieceAvailability, peerPieces);
            }

            return backgroundCandidates.OrderBy(c => c).First();
        }

        return null;
    }

    private int PickRarestFromList(List<int> candidates, IReadOnlyList<int> pieceAvailability, IReadOnlyList<BitArray> peerPieces)
    {
        if (pieceAvailability == null || pieceAvailability.Count == 0)
        {
            return candidates[0];
        }

        var minAvailability = int.MaxValue;
        var candidateAvailability = new List<(int PieceIndex, int Availability)>(candidates.Count);

        foreach (var pieceIndex in candidates)
        {
            var avail = RarestFirstPiecePicker.GetAvailability(pieceIndex, pieceAvailability, peerPieces);
            if (avail < minAvailability)
            {
                minAvailability = avail;
            }

            candidateAvailability.Add((pieceIndex, avail));
        }

        var tiedCandidates = candidateAvailability
            .Where(x => x.Availability == minAvailability)
            .Select(x => x.PieceIndex)
            .ToList();

        if (tiedCandidates.Count <= 1)
        {
            return tiedCandidates.Count == 1 ? tiedCandidates[0] : candidates[0];
        }

        var selectedIndex = _random.Next(tiedCandidates.Count);
        return tiedCandidates[selectedIndex];
    }

    private static bool PeerHasPiece(IReadOnlyList<BitArray> peerPieces, int pieceIndex)
    {
        for (var p = 0; p < peerPieces.Count; p++)
        {
            var pieces = peerPieces[p];
            if (pieces != null && pieceIndex < pieces.Length && pieces[pieceIndex])
            {
                return true;
            }
        }

        return false;
    }
}
