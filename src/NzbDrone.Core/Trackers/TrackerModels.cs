using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Trackers;

public interface ITrackerResponse
{
    bool Success { get; }
    string FailureReason { get; }
}

public class TrackerAnnounceRequest
{
    public string InfoHash { get; set; }
    public string PeerId { get; set; }
    public int Port { get; set; }
    public long Uploaded { get; set; }
    public long Downloaded { get; set; }
    public long Left { get; set; }
    public string Event { get; set; }
    public string TrackerUrl { get; set; }
    public bool Compact { get; set; } = true;
    public int NumWant { get; set; } = 50;
}

public class TrackerAnnounceResponse : ITrackerResponse
{
    public bool Success { get; set; }
    public int Interval { get; set; }
    public int MinInterval { get; set; }
    public int Complete { get; set; }
    public int Incomplete { get; set; }
    public List<TrackerPeer> Peers { get; set; } = new();
    public string FailureReason { get; set; }
    public string WarningMessage { get; set; }
}

public class TrackerPeer
{
    public string Ip { get; set; }
    public int Port { get; set; }
    public string PeerId { get; set; }
}

public class TrackerScrapeResponse : ITrackerResponse
{
    public bool Success { get; set; }
    public int Complete { get; set; }
    public int Incomplete { get; set; }
    public int Downloaded { get; set; }
    public string FailureReason { get; set; }
}

public class TrackerProviderDefinition : ProviderDefinition
{
}
