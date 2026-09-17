using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Peers.PiecePicker;

public class SequentialPiecePicker : IPiecePicker
{
    private readonly IPiecePicker _rarestFirstPicker;
    private readonly IRandomNumberGenerator _random;

    public SequentialPiecePicker(IPiecePicker rarestFirstPicker = null, IRandomNumberGenerator random = null)
    {
        _random = random ?? new RandomNumberGenerator();
        _rarestFirstPicker = rarestFirstPicker ?? new RarestFirstPiecePicker(_random);
    }

    public int? PickPiece(
        BitArray myPieces,
        BitArray peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow = 20,
        double rarestFirstRatio = 0.2)
    {
        return PickPiece(myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, null);
    }

    public int? PickPiece(
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow = 20,
        double rarestFirstRatio = 0.2)
    {
        return PickPiece(myPieces, peerPieces, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, null);
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
        return PickPiece(myPieces, list, pieceAvailability, sequential, lookaheadWindow, rarestFirstRatio, activePieces);
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
        if (myPieces == null || myPieces.Length == 0 || peerPieces == null || peerPieces.Count == 0)
        {
            return null;
        }

        if (!sequential)
        {
            return _rarestFirstPicker.PickPiece(myPieces, peerPieces, pieceAvailability, false, lookaheadWindow, rarestFirstRatio, activePieces);
        }

        var pieceCount = myPieces.Length;
        if (lookaheadWindow <= 0)
        {
            lookaheadWindow = 20;
        }

        // Find next contiguous missing piece starting from index 0
        var head = -1;
        for (var i = 0; i < pieceCount; i++)
        {
            if (!myPieces[i])
            {
                head = i;
                break;
            }
        }

        if (head == -1)
        {
            return null;
        }

        var windowEnd = Math.Min(head + lookaheadWindow, pieceCount);

        var windowCandidates = new List<int>();
        var outsideCandidates = new List<int>();

        for (var i = 0; i < pieceCount; i++)
        {
            if (myPieces[i])
            {
                continue;
            }

            if (!PeerHasPiece(peerPieces, i))
            {
                continue;
            }

            if (i >= head && i < windowEnd)
            {
                windowCandidates.Add(i);
            }
            else if (i >= windowEnd)
            {
                outsideCandidates.Add(i);
            }
        }

        var windowNonActive = activePieces != null && activePieces.Count > 0
            ? windowCandidates.Where(c => !activePieces.Contains(c)).ToList()
            : windowCandidates;

        var outsideNonActive = activePieces != null && activePieces.Count > 0
            ? outsideCandidates.Where(c => !activePieces.Contains(c)).ToList()
            : outsideCandidates;

        // Hybrid rarest-first swarm balancing:
        // Allocate a configurable portion (e.g. 20% rarest-first from outside window) so the client obtains tradeable pieces
        if (rarestFirstRatio > 0 && outsideNonActive.Count > 0)
        {
            var roll = _random.NextDouble();
            if (roll < rarestFirstRatio)
            {
                return PickRarestFromList(outsideNonActive, pieceAvailability, peerPieces);
            }
        }

        // Pick sequentially inside lookahead window (lowest index first)
        if (windowNonActive.Count > 0)
        {
            return windowNonActive.OrderBy(c => c).First();
        }

        // If no non-active candidates in window, but outside non-active exist, pick rarest outside window
        if (outsideNonActive.Count > 0)
        {
            return PickRarestFromList(outsideNonActive, pieceAvailability, peerPieces);
        }

        // Endgame mode / Stalled piece re-requesting:
        // When remaining missing pieces in active sequential window are pending/active from slow peers,
        // duplicate requests to other unchoked peers holding the piece (endgame strategy)
        if (windowCandidates.Count > 0)
        {
            return windowCandidates.OrderBy(c => c).First();
        }

        if (outsideCandidates.Count > 0)
        {
            return PickRarestFromList(outsideCandidates, pieceAvailability, peerPieces);
        }

        return null;
    }

    private int PickRarestFromList(List<int> candidates, IReadOnlyList<int> pieceAvailability, IReadOnlyList<BitArray> peerPieces)
    {
        if (_rarestFirstPicker is RarestFirstPiecePicker rarestFirst)
        {
            return rarestFirst.PickRarest(candidates, pieceAvailability, peerPieces);
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

        if (tiedCandidates.Count == 1)
        {
            return tiedCandidates[0];
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
