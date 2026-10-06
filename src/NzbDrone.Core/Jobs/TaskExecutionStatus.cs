namespace NzbDrone.Core.Jobs;

public enum TaskExecutionStatus
{
    None = 0,
    Success = 1,
    Failed = 2,
    Canceled = 3
}
