using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NLog.Config;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;

namespace NzbDrone.Core.Test.HealthCheck;

[TestFixture]
public class HealthCheckServiceTest
{
    private HealthCheckService _subject;

    [TearDown]
    public void TearDown()
    {
        LogManager.Configuration = null;
    }

    [Test]
    public void PerformChecks_should_return_empty_list_when_no_checks()
    {
        _subject = new HealthCheckService(new List<IHealthCheck>());

        var result = _subject.PerformChecks();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void PerformChecks_should_return_results_from_all_checks()
    {
        var check1 = Substitute.For<IHealthCheck>();
        check1.Check().Returns(HealthCheckResult.Ok("Check1"));

        var check2 = Substitute.For<IHealthCheck>();
        check2.Check().Returns(HealthCheckResult.Warning("Check2", "Something is wrong"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check1, check2 });

        var result = _subject.PerformChecks();

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void PerformChecks_should_include_ok_results()
    {
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Ok("OkCheck"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check });

        var result = _subject.PerformChecks();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.First().Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void PerformChecks_should_include_warning_results()
    {
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Warning("WarnCheck", "A warning occurred"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check });

        var result = _subject.PerformChecks();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.First().Type, Is.EqualTo(HealthCheckResultType.Warning));
    }

    [Test]
    public void PerformChecks_should_publish_HealthIssueEvent_with_isResolved_false_when_check_fails()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Error("DiskSpace", "Disk space is low"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

        _subject.PerformChecks();

        eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "DiskSpace" &&
            e.Message == "Disk space is low" &&
            !e.IsResolved));
    }

    [Test]
    public void PerformChecks_should_publish_HealthIssueEvent_with_isResolved_true_when_failed_check_recovers()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Warning("TrackerFailure", "Trackers failing"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

        _subject.PerformChecks();

        eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "TrackerFailure" &&
            !e.IsResolved));

        eventAggregator.ClearReceivedCalls();

        _subject.ExpireCache();
        check.Check().Returns(HealthCheckResult.Ok("TrackerFailure"));

        _subject.PerformChecks();

        eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            e.Source == "TrackerFailure" &&
            e.IsResolved));
    }

    [Test]
    public void PerformChecks_should_not_publish_duplicate_events_for_consecutive_identical_checks()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Error("DiskSpace", "Disk space is low"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

        _subject.PerformChecks();
        _subject.PerformChecks();
        _subject.PerformChecks();

        eventAggregator.Received(1).PublishEvent(Arg.Any<HealthIssueEvent>());

        eventAggregator.ClearReceivedCalls();

        _subject.ExpireCache();
        check.Check().Returns(HealthCheckResult.Ok("DiskSpace"));

        _subject.PerformChecks();
        _subject.PerformChecks();
        _subject.PerformChecks();

        eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e => e.IsResolved));
    }

    [Test]
    public void PerformChecks_should_publish_HealthIssueEvent_when_check_throws_unhandled_exception()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(_ => throw new InvalidOperationException("Check exploded"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

        _subject.PerformChecks();

        eventAggregator.Received(1).PublishEvent(Arg.Is<HealthIssueEvent>(e =>
            !e.IsResolved &&
            e.Message.Contains("Check exploded")));
    }

    [Test]
    public void PerformChecks_should_not_publish_event_when_consecutive_ok_checks_run()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Ok("DatabaseCheck"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check }, eventAggregator);

        _subject.PerformChecks();
        _subject.PerformChecks();

        eventAggregator.DidNotReceive().PublishEvent(Arg.Any<HealthIssueEvent>());
    }

    [Test]
    public void PerformChecks_should_return_cached_results_when_called_within_ttl()
    {
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Ok("CachedCheck"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check });

        var first = _subject.PerformChecks();
        var second = _subject.PerformChecks();

        check.Received(1).Check();
        Assert.That(first, Has.Count.EqualTo(1));
        Assert.That(second, Has.Count.EqualTo(1));
        Assert.That(second.First().Source, Is.EqualTo("CachedCheck"));
    }

    [Test]
    public void PerformChecks_should_reexecute_checks_after_cache_expires()
    {
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Ok("ExpiringCheck"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check })
        {
            CacheDuration = TimeSpan.FromMilliseconds(50)
        };

        _subject.PerformChecks();
        check.Received(1).Check();

        Thread.Sleep(70);

        _subject.PerformChecks();
        check.Received(2).Check();
    }

    [Test]
    public void ExpireCache_should_force_reexecution_of_checks()
    {
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Ok("ForceCheck"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check });

        _subject.PerformChecks();
        _subject.ExpireCache();
        _subject.PerformChecks();

        check.Received(2).Check();
    }

    [Test]
    public void PerformChecks_should_not_log_duplicate_warnings_on_repeated_identical_runs()
    {
        var ringBuffer = new RingBufferTarget();
        var config = new LoggingConfiguration();
        config.AddTarget("ringbuffer", ringBuffer);
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, ringBuffer);
        LogManager.Configuration = config;

        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Warning("WarnCheck", "Still failing"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check })
        {
            CacheDuration = TimeSpan.Zero
        };

        _subject.PerformChecks();
        _subject.PerformChecks();
        _subject.PerformChecks();

        var entries = ringBuffer.GetEntries(100, LogLevel.Trace)
            .Where(e => e.Logger.Contains("HealthCheckService") && e.Level == "Warn")
            .ToList();

        Assert.That(entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void PerformChecks_should_log_error_when_status_transitions_from_warning_to_error()
    {
        var ringBuffer = new RingBufferTarget();
        var config = new LoggingConfiguration();
        config.AddTarget("ringbuffer", ringBuffer);
        config.AddRule(LogLevel.Trace, LogLevel.Fatal, ringBuffer);
        LogManager.Configuration = config;

        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Warning("TransCheck", "Warning msg"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check })
        {
            CacheDuration = TimeSpan.Zero
        };

        _subject.PerformChecks();

        check.Check().Returns(HealthCheckResult.Error("TransCheck", "Error msg"));
        _subject.PerformChecks();

        var warnEntries = ringBuffer.GetEntries(100, LogLevel.Trace)
            .Where(e => e.Logger.Contains("HealthCheckService") && e.Level == "Warn")
            .ToList();
        var errorEntries = ringBuffer.GetEntries(100, LogLevel.Trace)
            .Where(e => e.Logger.Contains("HealthCheckService") && e.Level == "Error")
            .ToList();

        Assert.That(warnEntries, Has.Count.EqualTo(1));
        Assert.That(errorEntries, Has.Count.EqualTo(1));
    }

    [Test]
    public void PerformChecks_should_return_error_when_check_times_out()
    {
        var slowCheck = Substitute.For<IHealthCheck>();
        slowCheck.Check().Returns(_ =>
        {
            Thread.Sleep(500);
            return HealthCheckResult.Ok("SlowCheck");
        });

        _subject = new HealthCheckService(new List<IHealthCheck> { slowCheck })
        {
            CheckTimeout = TimeSpan.FromMilliseconds(50)
        };

        var results = _subject.PerformChecks();

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results.First().Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(results.First().Message, Does.Contain("timed out"));
    }

    [Test]
    public void PerformChecks_should_broadcast_SignalR_message_on_completion()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Ok("OkCheck"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check }, null, broadcaster);

        _subject.PerformChecks();

        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "HealthCheckCompleted" &&
            m.Action == ModelAction.Updated &&
            m.Body != null));
    }

    [Test]
    public void PerformChecks_should_not_broadcast_SignalR_message_on_cache_hit()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Ok("OkCheck"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check }, null, broadcaster)
        {
            CacheDuration = TimeSpan.FromSeconds(30)
        };

        _subject.PerformChecks();
        broadcaster.ClearReceivedCalls();

        _subject.PerformChecks();
        broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void PerformChecks_should_broadcast_SignalR_message_after_cache_expired()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var check = Substitute.For<IHealthCheck>();
        check.Check().Returns(HealthCheckResult.Ok("OkCheck"));

        _subject = new HealthCheckService(new List<IHealthCheck> { check }, null, broadcaster)
        {
            CacheDuration = TimeSpan.FromMilliseconds(10)
        };

        _subject.PerformChecks();
        broadcaster.ClearReceivedCalls();

        _subject.ExpireCache();
        _subject.PerformChecks();

        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "HealthCheckCompleted"));
    }
}
