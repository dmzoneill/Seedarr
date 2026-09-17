using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public interface IPieceStorage
{
    bool[] GetVerifiedPieces(string infoHash);

    bool IsPieceVerified(string infoHash, int pieceIndex);

    void MarkPieceVerified(string infoHash, int pieceIndex, long bytesDownloaded = 0);

    void MarkPiecesVerified(string infoHash, IEnumerable<int> pieceIndexes, long bytesDownloaded = 0);

    void MarkPieceCorrupted(string infoHash, int pieceIndex);

    bool IsPieceCorrupted(string infoHash, int pieceIndex);

    HashSet<int> GetCorruptedPieces(string infoHash);

    void SetVerifiedPieces(string infoHash, bool[] pieces);

    int[] GetPieceStates(string infoHash, int pieceCount, double progress = 0.0, HashSet<int> activePieces = null);

    void Clear(string infoHash);
}
