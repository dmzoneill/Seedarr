using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;

namespace NzbDrone.Core.Jobs;

public class Scheduler : BackgroundService
{
    private readonly ITaskManager _taskManager;
    private readonly Logger _logger;

    public Scheduler(ITaskManager taskManager)
    {
        _taskManager = taskManager;
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
                        _logger.Trace("Task due: {0}", next.TypeName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Scheduler tick error");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
