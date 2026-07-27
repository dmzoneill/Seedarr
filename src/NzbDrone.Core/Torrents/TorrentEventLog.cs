using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentEventLog : ModelBase
{
    public int TorrentId { get; set; }
    public DateTime TimeStamp { get; set; }
    public string Level { get; set; }
    public string Source { get; set; }
    public string Message { get; set; }
}
