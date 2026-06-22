using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Torrents;

public class TorrentFileResource : RestResource
{
    public int TorrentId { get; set; }
    public string Path { get; set; }
    public long Size { get; set; }
    public int PieceOffset { get; set; }
    public int PieceCount { get; set; }
}
