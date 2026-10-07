using System.Threading;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Authentication;

public class LoginRateLimiterCleanupTask : IScheduledTask
{
    private readonly ILoginRateLimiter _loginRateLimiter;

    public int DefaultInterval => 60;

    public LoginRateLimiterCleanupTask(ILoginRateLimiter loginRateLimiter)
    {
        _loginRateLimiter = loginRateLimiter;
    }

    public void Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loginRateLimiter.ClearExpired();
    }
}
