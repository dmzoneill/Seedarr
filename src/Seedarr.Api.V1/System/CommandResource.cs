using System;

namespace Seedarr.Api.V1.System;

public class CommandResource
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Status { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public string Message { get; set; }
}
