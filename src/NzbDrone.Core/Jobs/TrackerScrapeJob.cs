using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Jobs;

public class TrackerScrapeJob : IAsyncScheduledTask
{
    private readonly ITrackerScrapeService _trackerScrapeService;
    private readonly Logger _logger;

    public int DefaultInterval => 60;

    public TrackerScrapeJob(ITrackerScrapeService trackerScrapeService)
    {
        _trackerScrapeService = trackerScrapeService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(CancellationToken cancellationToken)
    {
        ExecuteAsync(cancellationToken).GetAwaiter().GetResult();
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Executing scheduled tracker scrape task");

        var scrapedCount = await _trackerScrapeService.ScrapeAllTorrentsAsync(cancellationToken);
        _logger.Info("Completed scheduled tracker scrape for {0} torrents", scrapedCount);
    }
}
