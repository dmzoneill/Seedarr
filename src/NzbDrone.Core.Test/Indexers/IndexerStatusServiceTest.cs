using System;
using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class IndexerStatusServiceTest
{
    private IndexerStatusService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new IndexerStatusService();
    }

    [Test]
    public void IsDisabled_should_return_false_initially()
    {
        Assert.That(_service.IsDisabled(1), Is.False);
    }

    [Test]
    public void RecordFailure_should_disable_indexer_with_exponential_backoff()
    {
        _service.RecordFailure(1, (int)HttpStatusCode.TooManyRequests, "Rate limited");

        Assert.That(_service.IsDisabled(1), Is.True);
        var status = _service.GetStatus(1);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(status.LastStatusCode, Is.EqualTo(429));
        Assert.That(status.DisabledTill, Is.Not.Null);
    }

    [Test]
    public void RecordSuccess_should_reset_indexer_failures_and_recovery()
    {
        _service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Service Unavailable");
        Assert.That(_service.IsDisabled(1), Is.True);

        _service.RecordSuccess(1);
        Assert.That(_service.IsDisabled(1), Is.False);
        var status = _service.GetStatus(1);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(0));
        Assert.That(status.DisabledTill, Is.Null);
    }

    [Test]
    public void CalculateBackoff_scales_exponentially()
    {
        Assert.That(_service.CalculateBackoff(1), Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(_service.CalculateBackoff(2), Is.EqualTo(TimeSpan.FromMinutes(15)));
        Assert.That(_service.CalculateBackoff(3), Is.EqualTo(TimeSpan.FromMinutes(30)));
        Assert.That(_service.CalculateBackoff(4), Is.EqualTo(TimeSpan.FromHours(1)));
        Assert.That(_service.CalculateBackoff(5), Is.EqualTo(TimeSpan.FromHours(2)));
        Assert.That(_service.CalculateBackoff(6), Is.EqualTo(TimeSpan.FromHours(4)));
        Assert.That(_service.CalculateBackoff(7), Is.EqualTo(TimeSpan.FromHours(8)));
        Assert.That(_service.CalculateBackoff(8), Is.EqualTo(TimeSpan.FromHours(24)));
        Assert.That(_service.CalculateBackoff(10), Is.EqualTo(TimeSpan.FromHours(24)));
    }

    [Test]
    public void IsDisabled_should_decay_consecutive_failures_when_backoff_window_has_elapsed()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 1");
        currentTime = currentTime.AddMinutes(1);
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 2");

        var statusBefore = service.GetStatus(1);
        Assert.That(statusBefore.ConsecutiveFailures, Is.EqualTo(2));
        Assert.That(service.IsDisabled(1), Is.True);

        // Advance time past the 15-minute backoff window
        currentTime = currentTime.AddMinutes(16);

        // When IsDisabled is checked after backoff expires, it decays failure and returns false
        Assert.That(service.IsDisabled(1), Is.False);

        var statusAfter = service.GetStatus(1);
        Assert.That(statusAfter.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(statusAfter.DisabledTill, Is.Null);
    }

    [Test]
    public void DecayFailures_should_reset_failure_metadata_when_decaying_to_zero()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        service.RecordFailure(1, (int)HttpStatusCode.BadGateway, "Bad Gateway");
        Assert.That(service.IsDisabled(1), Is.True);

        // Advance time past the 5-minute backoff window
        currentTime = currentTime.AddMinutes(6);

        var status = service.GetStatus(1);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(0));
        Assert.That(status.DisabledTill, Is.Null);
        Assert.That(status.InitialFailure, Is.Null);
        Assert.That(status.LastFailureMessage, Is.Null);
        Assert.That(status.LastStatusCode, Is.Null);
    }

    [Test]
    public void RecordFailure_after_decay_should_increment_from_decayed_count()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        // 3 consecutive failures -> 30 min backoff
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 1");
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 2");
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 3");

        Assert.That(service.GetStatus(1).ConsecutiveFailures, Is.EqualTo(3));

        // Advance time past 30 min backoff
        currentTime = currentTime.AddMinutes(31);

        // Next failure occurs after decay
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail after decay");

        // Failures should have decayed from 3 to 2, then incremented to 3 (not 4)
        var status = service.GetStatus(1);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(3));
        // Backoff for 3 failures should be 30 minutes, not 1 hour (4 failures)
        Assert.That(status.DisabledTill, Is.EqualTo(currentTime.Add(TimeSpan.FromMinutes(30))));
    }

    [Test]
    public void GetAllStatuses_should_decay_expired_statuses()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 1");
        service.RecordFailure(2, (int)HttpStatusCode.ServiceUnavailable, "Fail 2");

        currentTime = currentTime.AddMinutes(6);

        var statuses = service.GetAllStatuses();
        Assert.That(statuses[1].ConsecutiveFailures, Is.EqualTo(0));
        Assert.That(statuses[2].ConsecutiveFailures, Is.EqualTo(0));
    }
}
