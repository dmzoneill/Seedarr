using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.DownloadPlusPlus;

public enum TrackerProtocol
{
    Udp = 0,
    Http = 1,
    Https = 2
}

public enum TrackerHealthStatus
{
    Untested = 0,
    Alive = 1,
    Slow = 2,
    Offline = 3
}

public enum TrackerSourceType
{
    PublicList = 0,
    Prowlarr = 1,
    ReleaseMagnet = 2,
    Manual = 3
}

public class DownloadPlusPlusTracker : ModelBase
{
    public string Url { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public TrackerProtocol Protocol { get; set; }
    public TrackerHealthStatus Status { get; set; }
    public TrackerSourceType Source { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
    public DateTime? LastScraped { get; set; }
    public DateTime? LastSuccess { get; set; }
    public int SuccessfulScrapes { get; set; }
    public int FailedScrapes { get; set; }
    public int TotalSwarmsFound { get; set; }
    public bool Enabled { get; set; } = true;
}

public class SwarmBoostResult
{
    public int TorrentId { get; set; }
    public string TorrentName { get; set; } = string.Empty;
    public string InfoHash { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public bool Boosted { get; set; }
    public int AddedTrackersCount { get; set; }
    public List<string> AddedTrackers { get; set; } = new();
    public int TotalSeedersFound { get; set; }
    public int TotalLeechersFound { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class DownloadPlusPlusStatusSummary
{
    public int TotalTrackersMonitored { get; set; }
    public int AliveTrackersCount { get; set; }
    public int SlowTrackersCount { get; set; }
    public int OfflineTrackersCount { get; set; }
    public int UntestedTrackersCount { get; set; }
    public int ProwlarrTrackersCount { get; set; }
    public int PublicListTrackersCount { get; set; }
    public int TorrentsBoostedCount { get; set; }
    public int ExtraTrackersInjectedCount { get; set; }
    public DateTime? LastScanTime { get; set; }
    public DateTime? LastProwlarrHarvestTime { get; set; }
}

public class TorrentTrackerDetection
{
    public int TrackerId { get; set; }
    public string TrackerUrl { get; set; } = string.Empty;
    public string TrackerHost { get; set; } = string.Empty;
    public TrackerProtocol Protocol { get; set; }
    public TrackerSourceType Source { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public bool IsAttached { get; set; }
    public bool IsDetected { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public int LatencyMs { get; set; }
    public TrackerHealthStatus HealthStatus { get; set; }
    public string DetectionStatus { get; set; } = string.Empty;
}

public class TorrentTrackerInspectionResult
{
    public int TorrentId { get; set; }
    public string TorrentName { get; set; } = string.Empty;
    public string InfoHash { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public int TotalTrackersChecked { get; set; }
    public int AttachedTrackersCount { get; set; }
    public int DetectedTrackersCount { get; set; }
    public List<TorrentTrackerDetection> Detections { get; set; } = new();
}
