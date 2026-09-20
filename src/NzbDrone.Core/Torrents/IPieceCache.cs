using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public interface IPieceCache
{
    long MaxCacheSizeBytes { get; set; }

    long CurrentCacheSizeBytes { get; }

    int CachedPieceCount { get; }

    bool AddBlock(int torrentId, int pieceIndex, int blockOffset, byte[] data, int pieceLength);

    bool VerifyAndFlushPiece(
        int torrentId,
        int pieceIndex,
        byte[] expectedHash,
        IMultiFilePieceStorage storage,
        Torrent torrent,
        IList<TorrentFile> files,
        string baseDirectory = null);

    bool IsPieceComplete(int torrentId, int pieceIndex);

    bool IsPieceFlushed(int torrentId, int pieceIndex);

    bool ContainsPiece(int torrentId, int pieceIndex);

    byte[] GetPieceData(int torrentId, int pieceIndex);

    bool TryGetPieceData(int torrentId, int pieceIndex, out byte[] data);

    bool DiscardPiece(int torrentId, int pieceIndex);

    void ClearTorrent(int torrentId);

    void Clear();
}
