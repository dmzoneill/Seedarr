using System.Collections.Generic;
using NzbDrone.Core.Simulation.ClientBehavior;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Trackers;

public enum AnnounceEvent
{
    None = 0,
    Started = 1,
    Completed = 2,
    Stopped = 3
}

public interface ITrackerResponse
{
    bool Success { get; }
    string FailureReason { get; }
}

public class TrackerAnnounceRequest
{
    public string InfoHash { get; set; }
    public string PeerId { get; set; }
    public string UserAgent { get; set; }
    public string Key { get; set; }
    public int Port { get; set; }
    public long Uploaded { get; set; }
    public long Downloaded { get; set; }
    public long Left { get; set; }
    public AnnounceEvent Event { get; set; }

    public string EventString => Event switch
    {
        AnnounceEvent.Started => "started",
        AnnounceEvent.Completed => "completed",
        AnnounceEvent.Stopped => "stopped",
        _ => null
    };

    public string TrackerUrl { get; set; }
    public bool Compact { get; set; } = true;
    public int NumWant { get; set; } = 50;
    public bool IsPrivate { get; set; }
    public IClientProfile ClientProfile { get; set; }
    public long LastAnnouncedUploaded { get; set; }

    public TrackerAnnounceRequest Clone(string trackerUrl = null)
    {
        return new TrackerAnnounceRequest
        {
            InfoHash = InfoHash,
            PeerId = PeerId,
            UserAgent = UserAgent,
            Key = Key,
            Port = Port,
            Uploaded = Uploaded,
            Downloaded = Downloaded,
            Left = Left,
            Event = Event,
            TrackerUrl = trackerUrl ?? TrackerUrl,
            Compact = Compact,
            NumWant = NumWant,
            IsPrivate = IsPrivate,
            ClientProfile = ClientProfile,
            LastAnnouncedUploaded = LastAnnouncedUploaded
        };
    }
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
    public bool IsSeeder { get; set; }
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
