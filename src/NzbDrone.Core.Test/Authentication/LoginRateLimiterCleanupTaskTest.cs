using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class LoginRateLimiterCleanupTaskTest
{
    [Test]
    public void Execute_CallsClearExpiredOnLoginRateLimiter()
    {
        var limiter = Substitute.For<ILoginRateLimiter>();
        var task = new LoginRateLimiterCleanupTask(limiter);

        task.Execute();

        limiter.Received(1).ClearExpired();
    }

    [Test]
    public void DefaultInterval_IsSixtyMinutes()
    {
        var limiter = Substitute.For<ILoginRateLimiter>();
        var task = new LoginRateLimiterCleanupTask(limiter);

        Assert.That(task.DefaultInterval, Is.EqualTo(60));
    }
}
