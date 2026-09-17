using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentFile : ModelBase
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

    [Ignore]
    public long ByteOffset { get; set; }
}
