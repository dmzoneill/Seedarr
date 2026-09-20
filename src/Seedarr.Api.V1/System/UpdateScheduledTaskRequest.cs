namespace Seedarr.Api.V1.System;

/// <summary>
/// Request model for updating scheduled task interval and enabled state.
/// </summary>
public class UpdateScheduledTaskRequest
{
    public int Interval { get; set; }
    public bool IsEnabled { get; set; }
}
