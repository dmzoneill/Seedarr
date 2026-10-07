using System.Threading;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Authentication;

public class SessionRevocationCleanupTask : IScheduledTask
{
    private readonly ISessionRevocationService _sessionRevocationService;

    public int DefaultInterval => 60;

    public SessionRevocationCleanupTask(ISessionRevocationService sessionRevocationService)
    {
        _sessionRevocationService = sessionRevocationService;
    }

    public void Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessionRevocationService.ClearExpired();
    }
}
