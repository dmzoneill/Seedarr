using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public interface IPiecePicker
{
    HashSet<int> GetActivePieces(string infoHash);

    bool IsPieceActive(string infoHash, int pieceIndex);

    void MarkPieceActive(string infoHash, int pieceIndex);

    void MarkPieceInactive(string infoHash, int pieceIndex);

    void Clear(string infoHash);

    int? PickNextPiece(string infoHash, int pieceCount, bool[] verifiedPieces, HashSet<int> activePieces = null);
}
