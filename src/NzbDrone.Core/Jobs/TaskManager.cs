using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Jobs;

public interface ITaskManager
{
    IEnumerable<ScheduledTask> GetAll();
    ScheduledTask GetNextScheduled();
    void Update(int id, int interval, bool isEnabled);
    void UpdateLastExecution(string typeName);
    void RecordTaskStarted(string typeName);
    void RecordTaskStarted(string typeName, ScheduledTaskTriggerSource triggerSource);
    CancellationTokenSource RecordTaskStarted(string typeName, CancellationTokenSource cts, ScheduledTaskTriggerSource triggerSource);
    CancellationTokenSource RecordTaskStarted(string typeName, CancellationTokenSource cts, TimeSpan? timeout = null, ScheduledTaskTriggerSource triggerSource = ScheduledTaskTriggerSource.Scheduler);
    void RecordTaskFinished(string typeName, DateTime startTime);
    void RecordTaskFinished(string typeName, DateTime startTime, ScheduledTaskTriggerSource? triggerSource = null);
    void RecordTaskFailed(string typeName, DateTime startTime, Exception exception, ScheduledTaskTriggerSource? triggerSource = null);
    void RecordTaskFailed(string typeName, DateTime startTime, string errorMessage, string exceptionDetails = null, ScheduledTaskTriggerSource? triggerSource = null);
    bool IsRunning(string typeName);
    bool CancelTask(int id);
    bool CancelTask(string typeName);
    CancellationTokenSource GetCancellationTokenSource(string typeName);
    string GetTaskStatus(string typeName);
    bool IsCanceled(string typeName);
    DateTime GetNextExecution(string typeName);
    List<ScheduledTaskHistory> GetTaskHistory(int taskId, int limit = 50);
    List<ScheduledTaskHistory> GetTaskHistory(string typeName, int limit = 50);
}

public class TaskManager : ITaskManager, IHandle<ApplicationStartedEvent>
{
    private class TaskExecutionInfo
    {
        public DateTime StartTime { get; set; }
        public ScheduledTaskTriggerSource TriggerSource { get; set; }
        public CancellationTokenSource Cts { get; set; }
        public bool HistoryRecorded { get; set; }
    }

    private readonly IBasicRepository<ScheduledTask> _repository;
    private readonly IEnumerable<IScheduledTask> _scheduledTasks;
    private readonly IScheduledTaskHistoryRepository _historyRepository;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, bool> _activeTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _taskStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskExecutionInfo> _activeExecutionInfo = new(StringComparer.OrdinalIgnoreCase);

    public TaskManager(
        IBasicRepository<ScheduledTask> repository,
        IEnumerable<IScheduledTask> scheduledTasks)
        : this(repository, scheduledTasks, null)
    {
    }

    public TaskManager(
        IBasicRepository<ScheduledTask> repository,
        IEnumerable<IScheduledTask> scheduledTasks,
        IScheduledTaskHistoryRepository historyRepository)
    {
        _repository = repository;
        _scheduledTasks = scheduledTasks;
        _historyRepository = historyRepository;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool IsRunning(string typeName)
    {
        return _activeTasks.ContainsKey(typeName);
    }

    public IEnumerable<ScheduledTask> GetAll()
    {
        var tasks = _repository.All().ToList();
        foreach (var task in tasks)
        {
            if (task.LastExecution.Kind != DateTimeKind.Utc && task.LastExecution != DateTime.MinValue)
            {
                task.LastExecution = DateTime.SpecifyKind(task.LastExecution, DateTimeKind.Utc);
            }

            if (task.LastStartTime.HasValue && task.LastStartTime.Value.Kind != DateTimeKind.Utc)
            {
                task.LastStartTime = DateTime.SpecifyKind(task.LastStartTime.Value, DateTimeKind.Utc);
            }
        }

        return tasks;
    }

    public ScheduledTask GetNextScheduled()
    {
        return GetAll()
            .Where(t => t.IsEnabled)
            .OrderBy(t => t.LastExecution == DateTime.MinValue
                ? DateTime.UtcNow.AddMinutes(t.Interval)
                : t.LastExecution.AddMinutes(t.Interval))
            .FirstOrDefault();
    }

    public void Update(int id, int interval, bool isEnabled)
    {
        var task = _repository.Get(id);
        if (task != null)
        {
            task.Interval = Math.Max(1, interval);
            task.IsEnabled = isEnabled;
            _repository.Update(task);
        }
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
        RecordTaskStarted(typeName, ScheduledTaskTriggerSource.Scheduler);
    }

    public void RecordTaskStarted(string typeName, ScheduledTaskTriggerSource triggerSource)
    {
        RecordTaskStarted(typeName, null, TimeSpan.FromMinutes(10), triggerSource);
    }

    public CancellationTokenSource RecordTaskStarted(string typeName, CancellationTokenSource cts, ScheduledTaskTriggerSource triggerSource)
    {
        return RecordTaskStarted(typeName, cts, null, triggerSource);
    }

    public CancellationTokenSource RecordTaskStarted(string typeName, CancellationTokenSource cts, TimeSpan? timeout = null, ScheduledTaskTriggerSource triggerSource = ScheduledTaskTriggerSource.Scheduler)
    {
        if (!_activeTasks.TryAdd(typeName, true))
        {
            throw new InvalidOperationException($"Task '{typeName}' is already running");
        }

        cts ??= new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(10));
        _cancellationTokens[typeName] = cts;
        _taskStatuses[typeName] = "Running";
        _activeExecutionInfo[typeName] = new TaskExecutionInfo
        {
            StartTime = DateTime.UtcNow,
            TriggerSource = triggerSource,
            Cts = cts,
            HistoryRecorded = false
        };

        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase));

        if (task != null)
        {
            task.LastStartTime = DateTime.UtcNow;
            _repository.Update(task);
        }

        return cts;
    }

    public void RecordTaskFinished(string typeName, DateTime startTime)
    {
        RecordTaskFinished(typeName, startTime, null);
    }

    public void RecordTaskFinished(string typeName, DateTime startTime, ScheduledTaskTriggerSource? triggerSource = null)
    {
        _activeTasks.TryRemove(typeName, out _);

        if (_cancellationTokens.TryRemove(typeName, out var cts))
        {
            try
            {
                cts.Dispose();
            }
            catch
            {
            }
        }

        _activeExecutionInfo.TryRemove(typeName, out var execInfo);

        var isCanceled = (_taskStatuses.TryGetValue(typeName, out var status) && string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase)) ||
                         (execInfo?.Cts != null && execInfo.Cts.IsCancellationRequested);

        if (!isCanceled && status != "Failed")
        {
            _taskStatuses[typeName] = "Completed";
        }

        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(t.TypeName.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (task != null)
        {
            task.LastStartTime = startTime;
            task.LastExecution = DateTime.UtcNow;
            _repository.Update(task);
        }

        if (execInfo == null || !execInfo.HistoryRecorded)
        {
            var now = DateTime.UtcNow;
            var durationMs = (long)Math.Max(0, (now - startTime).TotalMilliseconds);
            var trigger = triggerSource ?? execInfo?.TriggerSource ?? ScheduledTaskTriggerSource.Scheduler;

            var historyStatus = isCanceled
                ? ScheduledTaskHistoryStatus.Canceled
                : (status == "Failed" ? ScheduledTaskHistoryStatus.Failed : ScheduledTaskHistoryStatus.Success);

            var history = new ScheduledTaskHistory
            {
                TaskId = task?.Id ?? 0,
                TypeName = task?.TypeName ?? typeName,
                StartedAt = startTime,
                FinishedAt = now,
                DurationMs = durationMs,
                Status = historyStatus,
                TriggerSource = trigger,
                ErrorMessage = isCanceled ? "Task execution was canceled" : null,
                ExceptionDetails = null
            };

            try
            {
                _historyRepository?.Insert(history);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to record task completion history for '{0}'", typeName);
            }
        }
    }

    public void RecordTaskFailed(string typeName, DateTime startTime, Exception exception, ScheduledTaskTriggerSource? triggerSource = null)
    {
        RecordTaskFailed(typeName, startTime, exception?.Message ?? "Task execution failed", exception?.ToString(), triggerSource);
    }

    public void RecordTaskFailed(string typeName, DateTime startTime, string errorMessage, string exceptionDetails = null, ScheduledTaskTriggerSource? triggerSource = null)
    {
        var now = DateTime.UtcNow;
        var durationMs = (long)Math.Max(0, (now - startTime).TotalMilliseconds);

        _activeExecutionInfo.TryGetValue(typeName, out var execInfo);
        var trigger = triggerSource ?? execInfo?.TriggerSource ?? ScheduledTaskTriggerSource.Scheduler;

        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(t.TypeName.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        var history = new ScheduledTaskHistory
        {
            TaskId = task?.Id ?? 0,
            TypeName = task?.TypeName ?? typeName,
            StartedAt = startTime,
            FinishedAt = now,
            DurationMs = durationMs,
            Status = ScheduledTaskHistoryStatus.Failed,
            TriggerSource = trigger,
            ErrorMessage = errorMessage,
            ExceptionDetails = exceptionDetails
        };

        try
        {
            _historyRepository?.Insert(history);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to record task failure history for '{0}'", typeName);
        }

        if (execInfo != null)
        {
            execInfo.HistoryRecorded = true;
        }

        _taskStatuses[typeName] = "Failed";
    }

    public bool CancelTask(int id)
    {
        var task = _repository.All().FirstOrDefault(t => t.Id == id);
        if (task == null)
        {
            return false;
        }

        return CancelTask(task.TypeName);
    }

    public bool CancelTask(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        var match = _cancellationTokens.Keys.FirstOrDefault(k =>
            string.Equals(k, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(k.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (match != null && _cancellationTokens.TryGetValue(match, out var cts))
        {
            _taskStatuses[match] = "Canceled";

            if (!cts.IsCancellationRequested)
            {
                try
                {
                    cts.Cancel();
                    _logger.Info("Cancellation requested for task '{0}'", match);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to cancel task '{0}'", match);
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    public CancellationTokenSource GetCancellationTokenSource(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var match = _cancellationTokens.Keys.FirstOrDefault(k =>
            string.Equals(k, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(k.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (match != null && _cancellationTokens.TryGetValue(match, out var cts))
        {
            return cts;
        }

        return null;
    }

    public string GetTaskStatus(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return "Idle";
        }

        var match = _taskStatuses.Keys.FirstOrDefault(k =>
            string.Equals(k, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(k.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (match != null && _taskStatuses.TryGetValue(match, out var status))
        {
            return status;
        }

        return IsRunning(typeName) ? "Running" : "Idle";
    }

    public bool IsCanceled(string typeName)
    {
        return string.Equals(GetTaskStatus(typeName), "Canceled", StringComparison.OrdinalIgnoreCase);
    }

    public DateTime GetNextExecution(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return DateTime.UtcNow;
        }

        var task = GetAll().FirstOrDefault(t =>
            string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.TypeName.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (task == null)
        {
            return DateTime.UtcNow;
        }

        return task.NextExecution;
    }

    public List<ScheduledTaskHistory> GetTaskHistory(int taskId, int limit = 50)
    {
        if (_historyRepository == null)
        {
            return new List<ScheduledTaskHistory>();
        }

        return _historyRepository.GetByTaskId(taskId, limit);
    }

    public List<ScheduledTaskHistory> GetTaskHistory(string typeName, int limit = 50)
    {
        if (_historyRepository == null)
        {
            return new List<ScheduledTaskHistory>();
        }

        return _historyRepository.GetByTypeName(typeName, limit);
    }

    public void Handle(ApplicationStartedEvent message)
    {
        var existing = _repository.All().ToList();

        // Reset any stale in-flight task start times from an unexpected shutdown/crash
        foreach (var task in existing)
        {
            var updated = false;

            if (task.LastStartTime.HasValue && task.LastStartTime.Value > task.LastExecution)
            {
                task.LastStartTime = task.LastExecution;
                updated = true;
            }

            if (task.LastExecution == DateTime.MinValue || task.LastExecution <= DateTime.MinValue.AddDays(1))
            {
                task.LastExecution = DateTime.UtcNow;
                updated = true;
            }

            if (updated)
            {
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
