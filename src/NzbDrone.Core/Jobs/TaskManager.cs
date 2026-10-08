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

[global::System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3427:Method overloads with default values should not be ambiguous", Justification = "Interface methods provide convenient optional parameter overloads for scheduler telemetry")]
[global::System.Diagnostics.CodeAnalysis.SuppressMessage("csharpsquid", "S3427", Justification = "Interface methods provide convenient optional parameter overloads for scheduler telemetry")]
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
    void RecordTaskFailed(string typeName, DateTime startTime, string errorMessage);
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

        public bool FailureHistoryInsertAttempted { get; set; }
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

    private string NormalizeTypeName(string typeName)
    {
        return ScheduledTaskTypeNameResolver.Normalize(typeName, _scheduledTasks, _repository?.All());
    }

    private string FindActiveKey(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return typeName;
        }

        var normalized = NormalizeTypeName(typeName);
        if (_activeTasks.ContainsKey(normalized) || _cancellationTokens.ContainsKey(normalized) || _taskStatuses.ContainsKey(normalized))
        {
            return normalized;
        }

        if (_activeTasks.ContainsKey(typeName) || _cancellationTokens.ContainsKey(typeName) || _taskStatuses.ContainsKey(typeName))
        {
            return typeName;
        }

        var match = _activeTasks.Keys
            .Concat(_cancellationTokens.Keys)
            .Concat(_taskStatuses.Keys)
            .FirstOrDefault(k =>
                string.Equals(k, typeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k.Split('.').LastOrDefault(), normalized, StringComparison.OrdinalIgnoreCase));

        return match ?? normalized;
    }

    public bool IsRunning(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        var normalized = NormalizeTypeName(typeName);
        if (_activeTasks.ContainsKey(normalized) || _activeTasks.ContainsKey(typeName))
        {
            return true;
        }

        return _activeTasks.Keys.Any(k =>
            string.Equals(k, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(k.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));
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
            .OrderBy(t => ScheduledTask.GetSchedulerOrderingKey(t.LastExecution, t.Interval))
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
        var normalized = NormalizeTypeName(typeName);
        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, normalized, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t.TypeName.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

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
        var key = NormalizeTypeName(typeName);

        if (!_activeTasks.TryAdd(key, true))
        {
            throw new InvalidOperationException($"Task '{typeName}' is already running");
        }

        cts ??= new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(10));
        _cancellationTokens[key] = cts;
        _taskStatuses[key] = "Running";
        _activeExecutionInfo[key] = new TaskExecutionInfo
        {
            StartTime = DateTime.UtcNow,
            TriggerSource = triggerSource,
            Cts = cts,
            HistoryRecorded = false
        };

        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, key, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t.TypeName.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

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
        var key = FindActiveKey(typeName);

        _activeTasks.TryRemove(key, out _);

        if (_cancellationTokens.TryRemove(key, out var cts))
        {
            try
            {
                cts.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to dispose CancellationTokenSource for task {0}", key);
            }
        }

        _activeExecutionInfo.TryRemove(key, out var execInfo);

        var isCanceled = (_taskStatuses.TryGetValue(key, out var status) && string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase)) ||
                        (execInfo?.Cts != null && execInfo.Cts.IsCancellationRequested);
        var isFailed = string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);

        if (!isCanceled && !isFailed)
        {
            _taskStatuses[key] = "Completed";
        }

        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, key, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t.TypeName.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (task != null)
        {
            task.LastStartTime = startTime;
            task.LastExecution = DateTime.UtcNow;
            if (isCanceled)
            {
                task.LastStatus = TaskExecutionStatus.Canceled;
                task.LastErrorMessage ??= "Task execution was canceled or timed out";
            }
            else if (!isFailed)
            {
                task.LastStatus = TaskExecutionStatus.Success;
                task.LastErrorMessage = null;
            }

            _repository.Update(task);
        }

        if (execInfo == null || !execInfo.HistoryRecorded)
        {
            var now = DateTime.UtcNow;
            var durationMs = (long)Math.Max(0, (now - startTime).TotalMilliseconds);
            var trigger = triggerSource ?? execInfo?.TriggerSource ?? ScheduledTaskTriggerSource.Scheduler;

            var historyStatus = isCanceled
                ? ScheduledTaskHistoryStatus.Canceled
                : (isFailed ? ScheduledTaskHistoryStatus.Failed : ScheduledTaskHistoryStatus.Success);

            var history = new ScheduledTaskHistory
            {
                TaskId = task?.Id ?? 0,
                TypeName = task?.TypeName ?? key,
                StartedAt = startTime,
                FinishedAt = now,
                DurationMs = durationMs,
                Status = historyStatus,
                TriggerSource = trigger,
                ErrorMessage = isCanceled ? "Task execution was canceled" : (isFailed ? task?.LastErrorMessage : null),
                ExceptionDetails = null
            };

            try
            {
                _historyRepository?.Insert(history);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to record task completion history for '{0}'", key);
            }
        }
    }

    public void RecordTaskFailed(string typeName, DateTime startTime, string errorMessage)
    {
        RecordTaskFailed(typeName, startTime, errorMessage, null, null);
    }

    public void RecordTaskFailed(string typeName, DateTime startTime, Exception exception, ScheduledTaskTriggerSource? triggerSource = null)
    {
        RecordTaskFailed(typeName, startTime, exception?.Message ?? "Task execution failed", exception?.ToString(), triggerSource);
    }

    public void RecordTaskFailed(string typeName, DateTime startTime, string errorMessage, string exceptionDetails = null, ScheduledTaskTriggerSource? triggerSource = null)
    {
        var key = FindActiveKey(typeName);
        var now = DateTime.UtcNow;
        var durationMs = (long)Math.Max(0, (now - startTime).TotalMilliseconds);

        _activeExecutionInfo.TryGetValue(key, out var execInfo);
        var trigger = triggerSource ?? execInfo?.TriggerSource ?? ScheduledTaskTriggerSource.Scheduler;

        var task = _repository.All()
            .FirstOrDefault(t => string.Equals(t.TypeName, key, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t.TypeName, typeName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(t.TypeName.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (task != null)
        {
            task.LastStartTime = startTime;
            task.LastExecution = now;
            task.LastStatus = TaskExecutionStatus.Failed;
            task.LastErrorMessage = errorMessage;
            _repository.Update(task);
        }

        var history = new ScheduledTaskHistory
        {
            TaskId = task?.Id ?? 0,
            TypeName = task?.TypeName ?? key,
            StartedAt = startTime,
            FinishedAt = now,
            DurationMs = durationMs,
            Status = ScheduledTaskHistoryStatus.Failed,
            TriggerSource = trigger,
            ErrorMessage = errorMessage,
            ExceptionDetails = exceptionDetails
        };

        var historyRecorded = false;
        try
        {
            _historyRepository?.Insert(history);
            historyRecorded = _historyRepository != null;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to record task failure history for '{0}'", key);
            if (execInfo != null)
            {
                execInfo.FailureHistoryInsertAttempted = true;
            }
        }

        if (execInfo != null && historyRecorded)
        {
            execInfo.HistoryRecorded = true;
        }

        _taskStatuses[key] = "Failed";
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

        var key = FindActiveKey(typeName);
        if (!_cancellationTokens.TryGetValue(key, out var cts))
        {
            var match = _cancellationTokens.Keys.FirstOrDefault(k =>
                string.Equals(k, typeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                key = match;
                _cancellationTokens.TryGetValue(key, out cts);
            }
        }

        if (cts != null)
        {
            _taskStatuses[key] = "Canceled";

            if (!cts.IsCancellationRequested)
            {
                try
                {
                    cts.Cancel();
                    _logger.Info("Cancellation requested for task '{0}'", key);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to cancel task '{0}'", key);
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

        var key = FindActiveKey(typeName);
        if (_cancellationTokens.TryGetValue(key, out var cts))
        {
            return cts;
        }

        var match = _cancellationTokens.Keys.FirstOrDefault(k =>
            string.Equals(k, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(k.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (match != null && _cancellationTokens.TryGetValue(match, out cts))
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

        var key = FindActiveKey(typeName);
        if (_taskStatuses.TryGetValue(key, out var status))
        {
            return status;
        }

        var match = _taskStatuses.Keys.FirstOrDefault(k =>
            string.Equals(k, typeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(k.Split('.').LastOrDefault(), typeName, StringComparison.OrdinalIgnoreCase));

        if (match != null && _taskStatuses.TryGetValue(match, out status))
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

        var normalized = NormalizeTypeName(typeName);
        var task = GetAll().FirstOrDefault(t =>
            string.Equals(t.TypeName, normalized, StringComparison.OrdinalIgnoreCase) ||
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

        var normalized = NormalizeTypeName(typeName);
        var history = _historyRepository.GetByTypeName(normalized, limit);
        if ((history == null || history.Count == 0) && !string.Equals(normalized, typeName, StringComparison.OrdinalIgnoreCase))
        {
            history = _historyRepository.GetByTypeName(typeName, limit);
        }

        return history ?? new List<ScheduledTaskHistory>();
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
