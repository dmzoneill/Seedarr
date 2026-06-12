using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Trackers;

public interface ITrackerProvider : IProvider
{
    TrackerAnnounceResponse Announce(TrackerAnnounceRequest request);
    TrackerScrapeResponse Scrape(string infoHash, string trackerUrl);
}
