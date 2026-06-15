using System;
using System.Net;

namespace NzbDrone.Core.Dht;

public class DhtNode
{
    public byte[] NodeId { get; set; }
    public IPEndPoint EndPoint { get; set; }
    public DateTime LastSeen { get; set; }
    public int FailCount { get; set; }
    public bool IsGood => FailCount < 3 && (DateTime.UtcNow - LastSeen).TotalMinutes < 15;
}
