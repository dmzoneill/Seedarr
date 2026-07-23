using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TrackerEntry : ModelBase
{
    public int TorrentId { get; set; }
    public string Url { get; set; }
    public int Tier { get; set; }
    public TrackerStatus Status { get; set; }
    public bool Enabled { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public long Downloaded { get; set; }
    public int TotalAnnounces { get; set; }
    public int SuccessfulAnnounces { get; set; }
    public int ConsecutiveFailures { get; set; }
    public double LastResponseTime { get; set; }
    public double AverageResponseTime { get; set; }
    public int AnnounceInterval { get; set; }
    public int MinAnnounceInterval { get; set; }
    public DateTime? LastAnnounce { get; set; }
    public DateTime? LastScrape { get; set; }
    public DateTime? NextAnnounce { get; set; }
    public string ErrorMessage { get; set; }
    public DateTime? LastErrorTime { get; set; }
    public string WarningMessage { get; set; }
}

public enum TrackerStatus
{
    Unknown = 0,
    Working = 1,
    Announcing = 2,
    Failed = 3,
    Disabled = 4
}
