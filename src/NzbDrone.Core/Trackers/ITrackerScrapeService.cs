using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Trackers;

public interface ITrackerScrapeService
{
    Task<TrackerScrapeResult> ScrapeTorrentAsync(int torrentId, CancellationToken cancellationToken = default);
    Task<int> ScrapeAllTorrentsAsync(CancellationToken cancellationToken = default);
}

public class TrackerScrapeResult
{
    public int TorrentId { get; set; }
    public bool Success { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public int Completed { get; set; }
    public string Status { get; set; }
    public int TrackersScraped { get; set; }
    public int SuccessfulScrapes { get; set; }
    public int FailedScrapes { get; set; }
    public string ErrorMessage { get; set; }
    public List<TrackerScrapeItemResult> TrackerResults { get; set; } = new();
}

public class TrackerScrapeItemResult
{
    public int TrackerId { get; set; }
    public string Url { get; set; }
    public bool Success { get; set; }
    public int Seeders { get; set; }
    public int Leechers { get; set; }
    public int Completed { get; set; }
    public long ResponseTimeMs { get; set; }
    public string ErrorMessage { get; set; }
}
