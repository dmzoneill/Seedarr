using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.SignalR;

namespace NzbDrone.Core.Jobs;

public class ScheduledTaskCommandExecutor : IExecute<ScheduledTaskCommand>
{
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly ITaskManager _taskManager;
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly Logger _logger;

    public ScheduledTaskCommandExecutor(
        IEnumerable<IScheduledTask> scheduledTasks,
        ITaskManager taskManager,
        IBroadcastSignalRMessage signalRBroadcaster = null)
    {
        _scheduledTasks = scheduledTasks ?? Enumerable.Empty<IScheduledTask>();
        _taskManager = taskManager;
        _signalRBroadcaster = signalRBroadcaster;
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
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        _taskManager.RecordTaskStarted(command.TaskName, cts, null, command.TriggerSource);

        var signalRTypeName = ScheduledTaskTypeNameResolver.ResolveForSignalR(command.TaskName, taskInstance, _scheduledTasks);

        var taskInfo = new
        {
            TypeName = signalRTypeName,
            Name = taskInstance.GetType().Name,
            TriggerSource = command.TriggerSource.ToString()
        };

        _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
        {
            Name = "TaskStarted",
            Action = Datastore.ModelAction.Created,
            Body = taskInfo
        });

        var completedSuccessfully = true;
        try
        {
            taskInstance.Execute(cts.Token);
            _logger.Info("Scheduled task completed successfully: {0}", command.TaskName);
        }
        catch (OperationCanceledException ex)
        {
            completedSuccessfully = false;
            _logger.Warn("Scheduled task was canceled or timed out: {0}", command.TaskName);
            _taskManager.RecordTaskFailed(command.TaskName, startTime, ex, command.TriggerSource);
            _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
            {
                Name = "TaskFailed",
                Action = Datastore.ModelAction.Updated,
                Body = new
                {
                    taskInfo.TypeName,
                    taskInfo.Name,
                    taskInfo.TriggerSource,
                    Error = ex.Message
                }
            });
            throw;
        }
        catch (Exception ex)
        {
            completedSuccessfully = false;
            _logger.Error(ex, "Scheduled task failed: {0}", command.TaskName);
            _taskManager.RecordTaskFailed(command.TaskName, startTime, ex, command.TriggerSource);
            _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
            {
                Name = "TaskFailed",
                Action = Datastore.ModelAction.Updated,
                Body = new
                {
                    taskInfo.TypeName,
                    taskInfo.Name,
                    taskInfo.TriggerSource,
                    Error = ex.Message
                }
            });
            throw;
        }
        finally
        {
            _taskManager.UpdateLastExecution(command.TaskName);
            _taskManager.RecordTaskFinished(command.TaskName, startTime, command.TriggerSource);

            if (completedSuccessfully)
            {
                _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
                {
                    Name = "TaskCompleted",
                    Action = Datastore.ModelAction.Updated,
                    Body = taskInfo
                });
            }
        }
    }
}
