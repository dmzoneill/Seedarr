using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Trackers;

public interface ITrackerProvider : IProvider
{
    TrackerAnnounceResponse Announce(TrackerAnnounceRequest request);
    Task<TrackerAnnounceResponse> AnnounceAsync(TrackerAnnounceRequest request, CancellationToken cancellationToken = default);
    TrackerScrapeResponse Scrape(string infoHash, string trackerUrl);
    Task<TrackerScrapeResponse> ScrapeAsync(string infoHash, string trackerUrl, CancellationToken cancellationToken = default);
    Dictionary<string, TrackerScrapeResponse> BatchScrape(IEnumerable<string> infoHashes, string trackerUrl);
    Task<Dictionary<string, TrackerScrapeResponse>> BatchScrapeAsync(IEnumerable<string> infoHashes, string trackerUrl, CancellationToken cancellationToken = default);
}
