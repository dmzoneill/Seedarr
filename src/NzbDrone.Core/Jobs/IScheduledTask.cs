using System.Threading;

namespace NzbDrone.Core.Jobs;

public interface IJob
{
    void Execute();

    void Execute(CancellationToken cancellationToken) => Execute();
}

public interface IScheduledTask : IJob
{
    int DefaultInterval { get; }
}
