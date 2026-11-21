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

        _subject.Execute(CancellationToken.None);

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
    public async Task Execute_reentrancy_lock_prevents_overlapping_execution()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult(true);
                return tcs.Task;
            });

        var firstTask = _subject.ExecuteAsync(CancellationToken.None);
        using var startedCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await started.Task.WaitAsync(startedCts.Token);

        await _subject.ExecuteAsync(CancellationToken.None);

        tcs.SetResult(true);
        await firstTask;

        await _trackerBoostService.Received(1).RunOptimizationCycleAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Execute_reentrancy_lock_is_released_after_execution_completes()
    {
        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        _subject.Execute(CancellationToken.None);
        _subject.Execute(CancellationToken.None);

        _trackerBoostService.Received(2).RunOptimizationCycleAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public void Execute_reentrancy_lock_is_released_when_service_throws_exception()
    {
        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Service failed"));

        // Should not throw as RunOptimizationCycleSafelyAsync catches exceptions
        Assert.DoesNotThrow(() => _subject.Execute(CancellationToken.None));

        // Subsequent call should succeed and acquire lock again
        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrow(() => _subject.Execute(CancellationToken.None));
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

    [Test]
    public async Task ExecuteAsync_awaits_optimization_cycle_without_blocking()
    {
        var tcs = new TaskCompletionSource<bool>();
        _trackerBoostService.RunOptimizationCycleAsync(Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        using var cts = new CancellationTokenSource();
        var executeTask = _subject.ExecuteAsync(cts.Token);

        Assert.That(executeTask.IsCompleted, Is.False);

        tcs.SetResult(true);
        await executeTask;

        await _trackerBoostService.Received(1).RunOptimizationCycleAsync(cts.Token);
    }
}
