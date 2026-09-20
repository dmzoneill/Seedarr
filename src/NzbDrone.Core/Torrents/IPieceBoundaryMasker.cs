using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public interface IPieceBoundaryMasker
{
    bool[] ComputeWantedPieces(Torrent torrent, IList<TorrentFile> files);
    long CalculateWantedSize(Torrent torrent, IList<TorrentFile> files);
    bool IsSelectiveDownload(IList<TorrentFile> files);
}
