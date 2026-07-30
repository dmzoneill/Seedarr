using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Peers;

public class PeerConnectionLog : ModelBase
{
    public string InfoHash { get; set; }
    public string TorrentName { get; set; }
    public string RemoteIp { get; set; }
    public int RemotePort { get; set; }
    public string PeerId { get; set; }
    public bool IsEncrypted { get; set; }
    public string EventType { get; set; }
    public DateTime Timestamp { get; set; }
}
