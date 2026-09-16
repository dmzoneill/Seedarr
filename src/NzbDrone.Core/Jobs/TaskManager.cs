using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Jobs;

public interface ITaskManager
{
    IEnumerable<ScheduledTask> GetAll();
    ScheduledTask GetNextScheduled();
    void UpdateLastExecution(string typeName);
    void RecordTaskStarted(string typeName);
    void RecordTaskFinished(string typeName, DateTime startTime);
    bool IsRunning(string typeName);
}

public class TaskManager : ITaskManager, IHandle<ApplicationStartedEvent>
{
    private readonly IBasicRepository<ScheduledTask> _repository;
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, bool> _activeTasks = new(StringComparer.OrdinalIgnoreCase);

    public TaskManager(
        IBasicRepository<ScheduledTask> repository,
        IEnumerable<IScheduledTask> scheduledTasks)
    {
        _repository = repository;
        _scheduledTasks = scheduledTasks;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool IsRunning(string typeName)
    {
        return _activeTasks.ContainsKey(typeName);
    }

    public IEnumerable<ScheduledTask> GetAll()
    {
        return _repository.All();
    }

    public ScheduledTask GetNextScheduled()
    {
        return _repository.All()
            .OrderBy(t => t.LastExecution.AddMinutes(t.Interval))
            .FirstOrDefault();
    }

    public void UpdateLastExecution(string typeName)
    {
        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

        if (task != null)
        {
            task.LastExecution = DateTime.UtcNow;
            _repository.Update(task);
        }
    }

    public void RecordTaskStarted(string typeName)
    {
        if (!_activeTasks.TryAdd(typeName, true))
        {
            throw new InvalidOperationException($"Task '{typeName}' is already running");
        }

        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

        if (task != null)
        {
            task.LastStartTime = DateTime.UtcNow;
            _repository.Update(task);
        }
    }

    public void RecordTaskFinished(string typeName, DateTime startTime)
    {
        _activeTasks.TryRemove(typeName, out _);

        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

        if (task != null)
        {
            task.LastStartTime = startTime;
            task.LastExecution = DateTime.UtcNow;
            _repository.Update(task);
        }
    }

    public void Handle(ApplicationStartedEvent message)
    {
        var existing = _repository.All().ToList();

        // Reset any stale in-flight task start times from an unexpected shutdown/crash
        foreach (var task in existing)
        {
            if (task.LastStartTime.HasValue && task.LastStartTime.Value > task.LastExecution)
            {
                task.LastStartTime = task.LastExecution;
                _repository.Update(task);
            }
        }

        foreach (var task in _scheduledTasks)
        {
            var typeName = task.GetType().FullName;
            var match = existing.FirstOrDefault(e =>
                string.Equals(e.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                _logger.Debug("Registering scheduled task: {0}", typeName);
                _repository.Insert(new ScheduledTask
                {
                    TypeName = typeName,
                    Interval = task.DefaultInterval,
                    LastExecution = DateTime.UtcNow
                });
            }
        }
    }
}
