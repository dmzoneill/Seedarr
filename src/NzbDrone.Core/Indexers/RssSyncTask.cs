using System;
using NLog;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Indexers;

public class RssSyncTask : IScheduledTask
{
    private readonly IRssSyncService _rssSyncService;
    private readonly Logger _logger;

    public int DefaultInterval => 15;

    public RssSyncTask(IRssSyncService rssSyncService)
    {
        _rssSyncService = rssSyncService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        _logger.Info("Starting scheduled RSS sync task");

        try
        {
            var grabbed = _rssSyncService?.Sync(isManual: false) ?? 0;
            _logger.Info("Scheduled RSS sync task completed: {0} release(s) grabbed", grabbed);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Scheduled RSS sync task failed");
        }
    }
}
