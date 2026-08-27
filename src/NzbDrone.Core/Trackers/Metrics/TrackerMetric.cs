using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers.Metrics;

public class TrackerMetric : ModelBase
{
    public string TrackerUrl { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Protocol { get; set; } = "http";
    public int Port { get; set; } = 80;
    public string Status { get; set; } = "Working";
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime? LastAnnounce { get; set; }
    public DateTime? LastScrape { get; set; }
    public DateTime? LastSuccess { get; set; }
    public DateTime? LastErrorTime { get; set; }
    public string LastErrorMessage { get; set; }
    public long TotalAnnounces { get; set; }
    public long SuccessfulAnnounces { get; set; }
    public long FailedAnnounces { get; set; }
    public long TotalScrapes { get; set; }
    public long SuccessfulScrapes { get; set; }
    public long FailedScrapes { get; set; }
    public long TotalUploaded { get; set; }
    public long TotalDownloaded { get; set; }
    public long TotalLeft { get; set; }
    public long SessionUploaded { get; set; }
    public long SessionDownloaded { get; set; }
    public int TotalTorrentsTracked { get; set; }
    public int LastSeeders { get; set; }
    public int LastLeechers { get; set; }
    public int LastPeers { get; set; }
    public long TotalPeersDiscovered { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public long LastResponseTimeMs { get; set; }
    public long MinResponseTimeMs { get; set; }
    public long MaxResponseTimeMs { get; set; }
    public int ConsecutiveFailures { get; set; }
}
