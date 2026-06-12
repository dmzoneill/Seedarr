using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Trackers.MultiTracker;

public interface IMultiTrackerManager
{
    TrackerAnnounceResponse Announce(TrackerAnnounceRequest request, List<List<string>> announceList);
    TrackerScrapeResponse Scrape(string infoHash, List<List<string>> announceList);
}

public class MultiTrackerManager : IMultiTrackerManager
{
    private readonly ITrackerProvider _httpTracker;
    private readonly ITrackerProvider _udpTracker;
    private readonly Logger _logger;

    public MultiTrackerManager(
        IEnumerable<ITrackerProvider> trackerProviders)
    {
        var providers = trackerProviders.ToList();
        _httpTracker = providers.FirstOrDefault(p => p.Name == "HTTP");
        _udpTracker = providers.FirstOrDefault(p => p.Name == "UDP");
        _logger = LogManager.GetCurrentClassLogger();
    }

    public TrackerAnnounceResponse Announce(TrackerAnnounceRequest request, List<List<string>> announceList)
    {
        foreach (var tier in announceList)
        {
            foreach (var trackerUrl in tier)
            {
                try
                {
                    request.TrackerUrl = trackerUrl;
                    var provider = GetProvider(trackerUrl);
                    if (provider == null)
                    {
                        continue;
                    }

                    var response = provider.Announce(request);
                    if (response.Success)
                    {
                        return response;
                    }

                    _logger.Warn("Tracker {0} failed: {1}", trackerUrl, response.FailureReason);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Tracker {0} error", trackerUrl);
                }
            }
        }

        return new TrackerAnnounceResponse
        {
            Success = false,
            FailureReason = "All trackers failed"
        };
    }

    public TrackerScrapeResponse Scrape(string infoHash, List<List<string>> announceList)
    {
        foreach (var tier in announceList)
        {
            foreach (var trackerUrl in tier)
            {
                try
                {
                    var provider = GetProvider(trackerUrl);
                    if (provider == null)
                    {
                        continue;
                    }

                    var response = provider.Scrape(infoHash, trackerUrl);
                    if (response.Success)
                    {
                        return response;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Scrape {0} error", trackerUrl);
                }
            }
        }

        return new TrackerScrapeResponse
        {
            Success = false,
            FailureReason = "All trackers failed"
        };
    }

    private ITrackerProvider GetProvider(string url)
    {
        if (url.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
        {
            return _udpTracker;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return _httpTracker;
        }

        _logger.Warn("Unknown tracker protocol: {0}", url);
        return null;
    }
}
