using System;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Torrents;

public class TrackerEntryResource : RestResource
{
    public int TorrentId { get; set; }
    public string Url { get; set; }
    public int Tier { get; set; }
    public string Status { get; set; }
    public bool Enabled { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public int Downloaded { get; set; }
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
