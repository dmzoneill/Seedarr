using System;
using System.Threading;
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
    private readonly SemaphoreSlim _executionLock = new(1, 1);

    public int DefaultInterval => 2;

    public TrackerBoostOptimizationTask(ITrackerBoostService trackerBoostService)
    {
        _trackerBoostService = trackerBoostService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        RunOptimizationCycleSafelyAsync("background").GetAwaiter().GetResult();
    }

    public void Execute(CancellationToken cancellationToken)
    {
        RunOptimizationCycleSafelyAsync("background", cancellationToken).GetAwaiter().GetResult();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        _ = RunOptimizationCycleSafelyAsync("startup");
    }

    private async Task RunOptimizationCycleSafelyAsync(string context, CancellationToken cancellationToken = default)
    {
        if (!await _executionLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.Debug("TrackerBoost {0} optimization cycle skipped: another execution is already in progress", context);
            return;
        }

        try
        {
            await _trackerBoostService.RunOptimizationCycleAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.Info("TrackerBoost {0} optimization cycle was canceled", context);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "TrackerBoost {0} optimization cycle encountered an issue", context);
        }
        finally
        {
            _executionLock.Release();
        }
    }
}

