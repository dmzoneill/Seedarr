using System;
using NLog;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.TrackerBoost;

public class TrackerBoostOptimizationTask : IScheduledTask, IHandle<ApplicationStartedEvent>
{
    private readonly ITrackerBoostService _trackerBoostService;
    private readonly Logger _logger;

    public int DefaultInterval => 2;

    public TrackerBoostOptimizationTask(ITrackerBoostService trackerBoostService)
    {
        _trackerBoostService = trackerBoostService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        try
        {
            _trackerBoostService.RunOptimizationCycleAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "TrackerBoost background optimization cycle encountered an issue");
        }
    }

    public void Handle(ApplicationStartedEvent message)
    {
        try
        {
            _trackerBoostService.RunOptimizationCycleAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "TrackerBoost startup optimization cycle encountered an issue");
        }
    }
}
