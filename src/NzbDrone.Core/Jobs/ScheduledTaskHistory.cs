using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Jobs;

public enum ScheduledTaskHistoryStatus
{
    Success = 0,
    Failed = 1,
    Canceled = 2
}

public enum ScheduledTaskTriggerSource
{
    Scheduler = 0,
    Manual = 1,
    Api = 2
}

public class ScheduledTaskHistory : ModelBase
{
    public int TaskId { get; set; }
    public string TypeName { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public long DurationMs { get; set; }
    public ScheduledTaskHistoryStatus Status { get; set; }
    public ScheduledTaskTriggerSource TriggerSource { get; set; }
    public string ErrorMessage { get; set; }
    public string ExceptionDetails { get; set; }
}
