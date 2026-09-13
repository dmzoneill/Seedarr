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
}
