using System;
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
}

public class TaskManager : ITaskManager, IHandle<ApplicationStartedEvent>
{
    private readonly IBasicRepository<ScheduledTask> _repository;
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly Logger _logger;

    public TaskManager(
        IBasicRepository<ScheduledTask> repository,
        IEnumerable<IScheduledTask> scheduledTasks)
    {
        _repository = repository;
        _scheduledTasks = scheduledTasks;
        _logger = LogManager.GetCurrentClassLogger();
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

    public void Handle(ApplicationStartedEvent message)
    {
        var existing = _repository.All().ToList();

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
