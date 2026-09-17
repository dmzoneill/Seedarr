using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Torrents;

public class TorrentFileResource : RestResource
{
    public int TorrentId { get; set; }
    public string Path { get; set; }
    public long Size { get; set; }
    public int PieceOffset { get; set; }
    public int PieceCount { get; set; }
    public bool IsPaddingFile { get; set; }
    public bool Wanted { get; set; } = true;
    public int Priority { get; set; }
    public long BytesCompleted { get; set; }
}
