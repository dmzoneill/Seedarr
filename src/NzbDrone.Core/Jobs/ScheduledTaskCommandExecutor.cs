using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Jobs;

public class ScheduledTaskCommandExecutor : IExecute<ScheduledTaskCommand>
{
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly ITaskManager _taskManager;
    private readonly Logger _logger;

    public ScheduledTaskCommandExecutor(
        IEnumerable<IScheduledTask> scheduledTasks,
        ITaskManager taskManager)
    {
        _scheduledTasks = scheduledTasks ?? Enumerable.Empty<IScheduledTask>();
        _taskManager = taskManager;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(ScheduledTaskCommand command)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.TaskName))
        {
            throw new ArgumentException("TaskName must not be empty", nameof(command));
        }

        var taskInstance = _scheduledTasks.FirstOrDefault(t =>
            string.Equals(t.GetType().FullName, command.TaskName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.GetType().Name, command.TaskName, StringComparison.OrdinalIgnoreCase));

        if (taskInstance == null)
        {
            throw new InvalidOperationException($"No task instance found for scheduled type: {command.TaskName}");
        }

        if (_taskManager.IsRunning(command.TaskName))
        {
            throw new InvalidOperationException($"Task '{command.TaskName}' is already running");
        }

        _logger.Info("Executing scheduled task via command queue: {0}", command.TaskName);
        var startTime = DateTime.UtcNow;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        _taskManager.RecordTaskStarted(command.TaskName, cts, null, command.TriggerSource);

        try
        {
            taskInstance.Execute(cts.Token);
            _logger.Info("Scheduled task completed successfully: {0}", command.TaskName);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Scheduled task was canceled or timed out: {0}", command.TaskName);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Scheduled task failed: {0}", command.TaskName);
            _taskManager.RecordTaskFailed(command.TaskName, startTime, ex, command.TriggerSource);
            throw;
        }
        finally
        {
            _taskManager.UpdateLastExecution(command.TaskName);
            _taskManager.RecordTaskFinished(command.TaskName, startTime, command.TriggerSource);
        }
    }
}
