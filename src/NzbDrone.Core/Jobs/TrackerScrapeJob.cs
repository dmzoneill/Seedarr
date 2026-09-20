using System;
using System.Threading;
using NLog;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Jobs;

public class TrackerScrapeJob : IScheduledTask
{
    private readonly ITrackerScrapeService _trackerScrapeService;
    private readonly Logger _logger;

    public int DefaultInterval => 60;

    public TrackerScrapeJob(ITrackerScrapeService trackerScrapeService)
    {
        _trackerScrapeService = trackerScrapeService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        Execute(CancellationToken.None);
    }

    public void Execute(CancellationToken cancellationToken)
    {
        _logger.Info("Executing scheduled tracker scrape task");

        try
        {
            var scrapedCount = _trackerScrapeService.ScrapeAllTorrentsAsync(cancellationToken).GetAwaiter().GetResult();
            _logger.Info("Completed scheduled tracker scrape for {0} torrents", scrapedCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to execute scheduled tracker scrape");
        }
    }
}
