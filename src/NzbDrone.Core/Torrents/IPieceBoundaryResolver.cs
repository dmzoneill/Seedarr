using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public interface IPieceBoundaryResolver
{
    List<FileSlice> ResolvePiece(int pieceIndex, int pieceLength, long totalSize, IList<TorrentFile> files);

    List<FileSlice> ResolveRange(long globalOffset, long length, IList<TorrentFile> files);
}
