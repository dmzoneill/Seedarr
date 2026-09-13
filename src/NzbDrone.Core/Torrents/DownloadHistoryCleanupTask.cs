using System;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryCleanupTask : IScheduledTask
{
    private readonly IDownloadHistoryService _historyService;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public int DefaultInterval => 1440; // 24 hours

    public DownloadHistoryCleanupTask(IDownloadHistoryService historyService, IConfigService configService)
    {
        _historyService = historyService;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        try
        {
            var retentionDays = _configService.HistoryRetentionDays;
            if (retentionDays > 0)
            {
                var pruned = _historyService.PruneHistory(retentionDays);
                if (pruned > 0)
                {
                    _logger.Info("Pruned {0} expired download history records (retention policy: {1} days)", pruned, retentionDays);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to prune download history");
        }
    }
}
