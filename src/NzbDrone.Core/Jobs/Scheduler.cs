using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Jobs;

public class Scheduler : BackgroundService
{
    private readonly ITaskManager _taskManager;
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly Logger _logger;

    public Scheduler(ITaskManager taskManager, IEnumerable<IScheduledTask> scheduledTasks)
    {
        _taskManager = taskManager;
        _scheduledTasks = scheduledTasks;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Info("Scheduler started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var next = _taskManager.GetNextScheduled();

                if (next != null)
                {
                    var dueAt = next.LastExecution.AddMinutes(next.Interval);

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
                        _taskManager.RecordTaskStarted(next.TypeName);

                        try
                        {
                            var taskInstance = _scheduledTasks.FirstOrDefault(t =>
                                string.Equals(t.GetType().FullName, next.TypeName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t.GetType().Name, next.TypeName, StringComparison.OrdinalIgnoreCase));

                            if (taskInstance != null)
                            {
                                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                                cts.CancelAfter(TimeSpan.FromMinutes(10));
                                await Task.Run(() => taskInstance.Execute(), cts.Token);
                                _logger.Debug("Scheduled task completed: {0}", next.TypeName);
                            }
                            else
                            {
                                _logger.Warn("No task instance found for scheduled type: {0}", next.TypeName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Scheduled task failed: {0}", next.TypeName);
                        }
                        finally
                        {
                            _taskManager.UpdateLastExecution(next.TypeName);
                            _taskManager.RecordTaskFinished(next.TypeName, startTime);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Scheduler tick error");
            }

            await Task.Delay(50, stoppingToken);
        }
    }
}
