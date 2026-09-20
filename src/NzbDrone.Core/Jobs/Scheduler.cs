using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.SignalR;

namespace NzbDrone.Core.Jobs;

public class Scheduler : BackgroundService
{
    private readonly ITaskManager _taskManager;
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly TimeSpan _startupDelay;
    private readonly TimeSpan _minJitter;
    private readonly TimeSpan _maxJitter;
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly Random _random = new();
    private readonly Logger _logger;

    public TimeSpan StartupDelay => _startupDelay;
    public TimeSpan MinJitter => _minJitter;
    public TimeSpan MaxJitter => _maxJitter;

    public Scheduler(ITaskManager taskManager, IEnumerable<IScheduledTask> scheduledTasks, IBroadcastSignalRMessage signalRBroadcaster = null)
        : this(taskManager, scheduledTasks, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), signalRBroadcaster)
    {
    }

    public Scheduler(
        ITaskManager taskManager,
        IEnumerable<IScheduledTask> scheduledTasks,
        TimeSpan startupDelay,
        TimeSpan minJitter,
        TimeSpan maxJitter,
        IBroadcastSignalRMessage signalRBroadcaster = null)
    {
        _taskManager = taskManager;
        _scheduledTasks = scheduledTasks;
        _startupDelay = startupDelay;
        _minJitter = minJitter;
        _maxJitter = maxJitter;
        _signalRBroadcaster = signalRBroadcaster;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public virtual TimeSpan GetJitter()
    {
        if (_maxJitter <= TimeSpan.Zero || _maxJitter < _minJitter)
        {
            return _minJitter > TimeSpan.Zero ? _minJitter : TimeSpan.Zero;
        }

        var minMs = (int)Math.Max(0, _minJitter.TotalMilliseconds);
        var maxMs = (int)Math.Max(minMs, _maxJitter.TotalMilliseconds);
        if (minMs == maxMs)
        {
            return TimeSpan.FromMilliseconds(minMs);
        }

        return TimeSpan.FromMilliseconds(_random.Next(minMs, maxMs + 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("Scheduler started");

        if (_startupDelay > TimeSpan.Zero)
        {
            _logger.Info("Scheduler startup grace delay of {0:N0}s before dispatching tasks", _startupDelay.TotalSeconds);
            try
            {
                await Task.Delay(_startupDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var next = _taskManager.GetNextScheduled();

                if (next != null)
                {
                    if (!next.IsEnabled)
                    {
                        await Task.Delay(50, stoppingToken);
                        continue;
                    }

                    var isUninitialized = next.LastExecution == DateTime.MinValue || next.LastExecution <= DateTime.MinValue.AddDays(1);
                    var dueAt = isUninitialized
                        ? DateTime.UtcNow
                        : next.LastExecution.AddMinutes(next.Interval);

                    if (dueAt <= DateTime.UtcNow)
                    {
                        if (_taskManager.IsRunning(next.TypeName))
                        {
                            _logger.Debug("Scheduled task already running: {0}", next.TypeName);
                            await Task.Delay(50, stoppingToken);
                            continue;
                        }

                        _logger.Debug("Executing scheduled task: {0}", next.TypeName);
                        var startTime = DateTime.UtcNow;
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        cts.CancelAfter(TimeSpan.FromMinutes(10));
                        _taskManager.RecordTaskStarted(next.TypeName, cts, null, ScheduledTaskTriggerSource.Scheduler);

                        var taskInstance = _scheduledTasks.FirstOrDefault(t =>
                            string.Equals(t.GetType().FullName, next.TypeName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.GetType().Name, next.TypeName, StringComparison.OrdinalIgnoreCase));

                        var taskInfo = new
                        {
                            TypeName = next.TypeName,
                            Name = taskInstance?.GetType().Name ?? next.TypeName,
                            TriggerSource = ScheduledTaskTriggerSource.Scheduler.ToString()
                        };

                        _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
                        {
                            Name = "TaskStarted",
                            Action = ModelAction.Created,
                            Body = taskInfo
                        });

                        try
                        {
                            if (taskInstance != null)
                            {
                                await Task.Run(() => taskInstance.Execute(cts.Token), cts.Token);
                                _logger.Debug("Scheduled task completed: {0}", next.TypeName);
                            }
                            else
                            {
                                _logger.Warn("No task instance found for scheduled type: {0}", next.TypeName);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.Warn("Scheduled task was canceled or timed out: {0}", next.TypeName);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Scheduled task failed: {0}", next.TypeName);
                            _taskManager.RecordTaskFailed(next.TypeName, startTime, ex, ScheduledTaskTriggerSource.Scheduler);
                        }
                        finally
                        {
                            _taskManager.UpdateLastExecution(next.TypeName);
                            _taskManager.RecordTaskFinished(next.TypeName, startTime, ScheduledTaskTriggerSource.Scheduler);

                            _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
                            {
                                Name = "TaskCompleted",
                                Action = ModelAction.Updated,
                                Body = taskInfo
                            });
                        }

                        // Stagger consecutive/overdue task runs with jitter to prevent CPU/IO spikes
                        var nextPending = _taskManager.GetNextScheduled();
                        var isNextOverdue = nextPending != null &&
                            (nextPending.LastExecution == DateTime.MinValue ||
                            nextPending.LastExecution.AddMinutes(nextPending.Interval) <= DateTime.UtcNow);

                        if (isNextOverdue)
                        {
                            var jitter = GetJitter();
                            if (jitter > TimeSpan.Zero)
                            {
                                _logger.Debug("Applying scheduler jitter delay of {0:N1}s before next overdue task", jitter.TotalSeconds);
                                await Task.Delay(jitter, stoppingToken);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Scheduler tick error");
            }

            await Task.Delay(50, stoppingToken);
        }
    }
}
