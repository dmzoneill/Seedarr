using System;
using System.Net;

namespace NzbDrone.Core.Dht;

public class DhtNode
{
    public byte[] NodeId { get; set; }
    public IPEndPoint EndPoint { get; set; }
    public DateTime LastSeen { get; set; }
    public int FailCount { get; set; }
    public bool IsBad => FailCount >= 3;
    public bool IsQuestionable => !IsBad && (DateTime.UtcNow - LastSeen).TotalMinutes >= 15;
    public bool IsGood => !IsBad && (DateTime.UtcNow - LastSeen).TotalMinutes < 15;
}
