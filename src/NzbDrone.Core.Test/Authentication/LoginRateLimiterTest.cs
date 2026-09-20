using System;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class LoginRateLimiterTest
{
    private LoginRateLimiter _rateLimiter;

    [SetUp]
    public void SetUp()
    {
        _rateLimiter = new LoginRateLimiter(maxFailedAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15), failureWindow: TimeSpan.FromMinutes(15));
    }

    [Test]
    public void IsRateLimited_WhenNoAttemptsRecorded_ReturnsFalse()
    {
        var isLimited = _rateLimiter.IsRateLimited("192.168.1.50", out var retryAfter);

        Assert.That(isLimited, Is.False);
        Assert.That(retryAfter, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void RecordFailedAttempt_BelowThreshold_DoesNotLockOut()
    {
        for (var i = 0; i < 4; i++)
        {
            _rateLimiter.RecordFailedAttempt("192.168.1.50");
        }

        Assert.That(_rateLimiter.GetFailedAttempts("192.168.1.50"), Is.EqualTo(4));
        Assert.That(_rateLimiter.IsRateLimited("192.168.1.50", out _), Is.False);
    }

    [Test]
    public void RecordFailedAttempt_ReachingThreshold_ActivatesLockout()
    {
        for (var i = 0; i < 5; i++)
        {
            _rateLimiter.RecordFailedAttempt("192.168.1.50");
        }

        var isLimited = _rateLimiter.IsRateLimited("192.168.1.50", out var retryAfter);

        Assert.That(isLimited, Is.True);
        Assert.That(retryAfter.TotalSeconds, Is.GreaterThan(0));
    }

    [Test]
    public void RecordSuccessfulLogin_ResetsFailureCounter()
    {
        for (var i = 0; i < 4; i++)
        {
            _rateLimiter.RecordFailedAttempt("192.168.1.50");
        }

        Assert.That(_rateLimiter.GetFailedAttempts("192.168.1.50"), Is.EqualTo(4));

        _rateLimiter.RecordSuccessfulLogin("192.168.1.50");

        Assert.That(_rateLimiter.GetFailedAttempts("192.168.1.50"), Is.EqualTo(0));
        Assert.That(_rateLimiter.IsRateLimited("192.168.1.50", out _), Is.False);
    }

    [Test]
    public void IsRateLimited_DifferentIps_AreTrackedIndependently()
    {
        for (var i = 0; i < 5; i++)
        {
            _rateLimiter.RecordFailedAttempt("10.0.0.1");
        }

        Assert.That(_rateLimiter.IsRateLimited("10.0.0.1", out _), Is.True);
        Assert.That(_rateLimiter.IsRateLimited("10.0.0.2", out _), Is.False);
        Assert.That(_rateLimiter.GetFailedAttempts("10.0.0.2"), Is.EqualTo(0));
    }

    [Test]
    public async Task IsRateLimited_AfterLockoutExpires_ResetsAndReturnsFalse()
    {
        var shortLimiter = new LoginRateLimiter(
            maxFailedAttempts: 2,
            lockoutDuration: TimeSpan.FromMilliseconds(50),
            failureWindow: TimeSpan.FromMinutes(1));

        shortLimiter.RecordFailedAttempt("192.168.1.99");
        shortLimiter.RecordFailedAttempt("192.168.1.99");

        Assert.That(shortLimiter.IsRateLimited("192.168.1.99", out _), Is.True);

        await Task.Delay(100);

        Assert.That(shortLimiter.IsRateLimited("192.168.1.99", out _), Is.False);
        Assert.That(shortLimiter.GetFailedAttempts("192.168.1.99"), Is.EqualTo(0));
    }

    [Test]
    public void Clear_RemovesAllTrackedIps()
    {
        _rateLimiter.RecordFailedAttempt("192.168.1.1");
        _rateLimiter.RecordFailedAttempt("192.168.1.2");

        _rateLimiter.Clear();

        Assert.That(_rateLimiter.GetFailedAttempts("192.168.1.1"), Is.EqualTo(0));
        Assert.That(_rateLimiter.GetFailedAttempts("192.168.1.2"), Is.EqualTo(0));
    }

    [Test]
    public void ClearExpired_RemovesOldExpiredRecords()
    {
        var shortWindowLimiter = new LoginRateLimiter(
            maxFailedAttempts: 5,
            lockoutDuration: TimeSpan.FromMinutes(1),
            failureWindow: TimeSpan.FromMilliseconds(50));

        shortWindowLimiter.RecordFailedAttempt("192.168.1.20");

        System.Threading.Thread.Sleep(80);

        shortWindowLimiter.ClearExpired();

        Assert.That(shortWindowLimiter.GetFailedAttempts("192.168.1.20"), Is.EqualTo(0));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void HandleNullOrWhitespaceIp_DoesNotThrow(string ip)
    {
        Assert.DoesNotThrow(() =>
        {
            _rateLimiter.RecordFailedAttempt(ip);
            _rateLimiter.RecordSuccessfulLogin(ip);
            _rateLimiter.Reset(ip);
            var isLimited = _rateLimiter.IsRateLimited(ip, out var retryAfter);
            Assert.That(isLimited, Is.False);
            Assert.That(retryAfter, Is.EqualTo(TimeSpan.Zero));
            Assert.That(_rateLimiter.GetFailedAttempts(ip), Is.EqualTo(0));
        });
    }
}
