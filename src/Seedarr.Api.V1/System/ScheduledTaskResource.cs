using System;

namespace Seedarr.Api.V1.System;

public class ScheduledTaskResource
{
    public int Id { get; set; }
    public string TypeName { get; set; }
    public string Name { get; set; }
    public int Interval { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? LastExecution { get; set; }
    public DateTime? LastStartTime { get; set; }
    public TimeSpan? LastDuration { get; set; }
    public DateTime? NextExecution { get; set; }
    public bool IsRunning { get; set; }
}
