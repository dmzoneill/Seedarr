using System;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Peers;

public class PeerConnectionLogResource : RestResource
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
