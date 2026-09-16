namespace NzbDrone.Core.Jobs;

public interface IJob
{
    void Execute();
}

public interface IScheduledTask : IJob
{
    int DefaultInterval { get; }
}
