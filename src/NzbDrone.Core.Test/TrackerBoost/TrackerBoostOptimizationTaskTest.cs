using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.TrackerBoost;

namespace NzbDrone.Core.Test.TrackerBoost;

[TestFixture]
public class TrackerBoostOptimizationTaskTest
{
    private ITrackerBoostService _trackerBoostService;
    private TrackerBoostOptimizationTask _subject;

    [SetUp]
    public void SetUp()
    {
        _trackerBoostService = Substitute.For<ITrackerBoostService>();
        _subject = new TrackerBoostOptimizationTask(_trackerBoostService);
    }

    [Test]
    public void Execute_synchronously_awaits_optimization_cycle()
    {
        var executed = false;
        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(50);
                executed = true;
            });

        _subject.Execute();

        Assert.That(executed, Is.True);
        _trackerBoostService.Received(1).RunOptimizationCycleAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Execute_with_cancellation_token_passes_token_to_service()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _trackerBoostService.RunOptimizationCycleAsync(token).Returns(Task.CompletedTask);

        _subject.Execute(token);

        _trackerBoostService.Received(1).RunOptimizationCycleAsync(token);
    }

    [Test]
    public void Execute_reentrancy_lock_prevents_overlapping_execution()
    {
        var tcs = new TaskCompletionSource<bool>();
        var concurrentRunAttempted = false;

        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        // Start first execution in background thread to hold the lock
        var firstTask = Task.Run(() => _subject.Execute());

        // Wait briefly for first execution to acquire lock
        Thread.Sleep(50);

        // Attempt a second concurrent execution
        _subject.Execute();
        concurrentRunAttempted = true;

        // Release the first execution
        tcs.SetResult(true);
        firstTask.Wait(1000);

        Assert.That(concurrentRunAttempted, Is.True);
        // Only 1 cycle should have been run by the service; second execution should have been skipped
        _trackerBoostService.Received(1).RunOptimizationCycleAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Execute_reentrancy_lock_is_released_after_execution_completes()
    {
        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _subject.Execute();
        _subject.Execute();

        _trackerBoostService.Received(2).RunOptimizationCycleAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Execute_reentrancy_lock_is_released_when_service_throws_exception()
    {
        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Service failed"));

        // Should not throw as RunOptimizationCycleSafelyAsync catches exceptions
        Assert.DoesNotThrow(() => _subject.Execute());

        // Subsequent call should succeed and acquire lock again
        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrow(() => _subject.Execute());
        _trackerBoostService.Received(2).RunOptimizationCycleAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Handle_ApplicationStartedEvent_runs_optimization_cycle()
    {
        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _subject.Handle(new ApplicationStartedEvent());

        // Allow async startup task to start
        Thread.Sleep(50);

        _trackerBoostService.Received(1).RunOptimizationCycleAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void DefaultInterval_is_two_minutes()
    {
        Assert.That(_subject.DefaultInterval, Is.EqualTo(2));
    }
}
