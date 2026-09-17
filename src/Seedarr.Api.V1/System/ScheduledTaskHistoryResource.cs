using System;

namespace Seedarr.Api.V1.System;

public class ScheduledTaskHistoryResource
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string TypeName { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public long DurationMs { get; set; }
    public string Status { get; set; }
    public string TriggerSource { get; set; }
    public string ErrorMessage { get; set; }
    public string ExceptionDetails { get; set; }
}
