using System.Collections;
using System.Collections.Generic;

namespace NzbDrone.Core.Peers.PiecePicker;

public interface IPiecePicker
{
    int? PickPiece(
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow = 20,
        double rarestFirstRatio = 0.2);

    int? PickPiece(
        BitArray myPieces,
        BitArray peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow = 20,
        double rarestFirstRatio = 0.2);

    int? PickPiece(
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow,
        double rarestFirstRatio,
        IReadOnlyCollection<int> activePieces);

    int? PickPiece(
        BitArray myPieces,
        BitArray peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow,
        double rarestFirstRatio,
        IReadOnlyCollection<int> activePieces);

    int? PickPiece(
        BitArray myPieces,
        IReadOnlyList<BitArray> peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow,
        double rarestFirstRatio,
        IReadOnlyCollection<int> activePieces,
        bool firstLastPiecePrio,
        IReadOnlyCollection<int> customBoundaryPieces = null);

    int? PickPiece(
        BitArray myPieces,
        BitArray peerPieces,
        IReadOnlyList<int> pieceAvailability,
        bool sequential,
        int lookaheadWindow,
        double rarestFirstRatio,
        IReadOnlyCollection<int> activePieces,
        bool firstLastPiecePrio,
        IReadOnlyCollection<int> customBoundaryPieces = null);
}
