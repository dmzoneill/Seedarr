using System;
using System.Threading.Tasks;
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
        _ = RunOptimizationCycleSafelyAsync("background");
    }

    public void Handle(ApplicationStartedEvent message)
    {
        _ = RunOptimizationCycleSafelyAsync("startup");
    }

    private async Task RunOptimizationCycleSafelyAsync(string context)
    {
        try
        {
            await _trackerBoostService.RunOptimizationCycleAsync();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "TrackerBoost {0} optimization cycle encountered an issue", context);
        }
    }
}
