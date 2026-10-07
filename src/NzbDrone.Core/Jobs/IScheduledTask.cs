using System.Threading;

namespace NzbDrone.Core.Jobs;

public interface IJob
{
    void Execute(CancellationToken cancellationToken);

    void Execute()
    {
        Execute(CancellationToken.None);
    }
}

public interface IScheduledTask : IJob
{
    int DefaultInterval { get; }
}
