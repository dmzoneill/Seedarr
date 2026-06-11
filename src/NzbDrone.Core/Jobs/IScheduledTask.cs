namespace NzbDrone.Core.Jobs;

public interface IScheduledTask
{
    int DefaultInterval { get; }
}
