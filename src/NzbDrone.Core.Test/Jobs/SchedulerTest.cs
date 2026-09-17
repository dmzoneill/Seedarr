using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Test.Jobs;

[TestFixture]
public class SchedulerTest
{
    private ITaskManager _taskManager;
    private Scheduler _subject;

    private class TestScheduledTask : IScheduledTask
    {
        public int DefaultInterval => 1;
        public int ExecuteCount { get; private set; }

        public void Execute()
        {
            ExecuteCount++;
        }
    }

    private class ThrowingScheduledTask : IScheduledTask
    {
        public int DefaultInterval => 1;

        public void Execute()
        {
            throw new InvalidOperationException("Task failed");
        }
    }

    [SetUp]
    public void SetUp()
    {
        _taskManager = Substitute.For<ITaskManager>();
    }

    [Test]
    public async Task ExecuteAsync_should_stop_when_cancellation_requested()
    {
        _taskManager.GetNextScheduled().Returns((ScheduledTask)null);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask>(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(200);

        Assert.That(cts.IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_execute_due_task()
    {
        var testTask = new TestScheduledTask();
        var scheduled = new ScheduledTask
        {
            TypeName = typeof(TestScheduledTask).FullName,
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled, scheduled, (ScheduledTask)null);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { testTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        Assert.That(testTask.ExecuteCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_should_not_execute_future_task()
    {
        var testTask = new TestScheduledTask();
        var scheduled = new ScheduledTask
        {
            TypeName = typeof(TestScheduledTask).FullName,
            Interval = 9999,
            LastExecution = DateTime.UtcNow
        };

        _taskManager.GetNextScheduled().Returns(scheduled);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { testTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(200);

        Assert.That(testTask.ExecuteCount, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteAsync_should_handle_task_throwing_exception()
    {
        var throwingTask = new ThrowingScheduledTask();
        var scheduled = new ScheduledTask
        {
            TypeName = typeof(ThrowingScheduledTask).FullName,
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled, (ScheduledTask)null);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { throwingTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        _taskManager.Received().UpdateLastExecution(typeof(ThrowingScheduledTask).FullName);
    }

    [Test]
    public async Task ExecuteAsync_should_update_last_execution_when_no_task_instance_found()
    {
        var scheduled = new ScheduledTask
        {
            TypeName = "NonExistent.TaskType",
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled, (ScheduledTask)null);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask>(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        _taskManager.Received().UpdateLastExecution("NonExistent.TaskType");
    }

    [Test]
    public async Task ExecuteAsync_should_not_starve_subsequent_tasks_when_unregistered_task_encountered()
    {
        var orphanedTask = new ScheduledTask
        {
            TypeName = "NonExistent.TaskType",
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };
        var validTask = new TestScheduledTask();
        var validScheduled = new ScheduledTask
        {
            TypeName = typeof(TestScheduledTask).FullName,
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-5)
        };

        _taskManager.GetNextScheduled().Returns(orphanedTask, validScheduled, (ScheduledTask)null);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { validTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        _taskManager.Received().UpdateLastExecution("NonExistent.TaskType");
    }

    [Test]
    public async Task ExecuteAsync_should_respect_startup_grace_delay_before_dispatching_overdue_tasks()
    {
        var testTask = new TestScheduledTask();
        var scheduled = new ScheduledTask
        {
            TypeName = typeof(TestScheduledTask).FullName,
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled);
        _subject = new Scheduler(
            _taskManager,
            new List<IScheduledTask> { testTask },
            startupDelay: TimeSpan.FromSeconds(5),
            minJitter: TimeSpan.Zero,
            maxJitter: TimeSpan.Zero);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(100);

        Assert.That(testTask.ExecuteCount, Is.EqualTo(0));
    }

    [Test]
    public void GetJitter_should_return_duration_within_jitter_bounds()
    {
        var min = TimeSpan.FromSeconds(5);
        var max = TimeSpan.FromSeconds(15);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask>(), TimeSpan.Zero, min, max);

        for (var i = 0; i < 50; i++)
        {
            var jitter = _subject.GetJitter();
            Assert.That(jitter, Is.GreaterThanOrEqualTo(min));
            Assert.That(jitter, Is.LessThanOrEqualTo(max));
        }
    }

    [Test]
    public void CalculateNextExecution_for_uninitialized_task_does_not_produce_dates_in_distant_past()
    {
        var now = DateTime.UtcNow;
        var next = ScheduledTask.CalculateNextExecution(DateTime.MinValue, 15, now);

        Assert.That(next.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(next, Is.GreaterThanOrEqualTo(now));
        Assert.That(next.Year, Is.EqualTo(now.Year));
    }

    [Test]
    public void CalculateNextExecution_should_calculate_drift_free_advancing_missed_intervals()
    {
        var baseTime = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
        var now = baseTime.AddMinutes(65); // 1 hour 5 mins later

        // Interval 30 mins: 10:00 -> missed 10:30, 11:00 -> next is 11:30
        var next = ScheduledTask.CalculateNextExecution(baseTime, 30, now);

        Assert.That(next, Is.EqualTo(new DateTime(2026, 9, 17, 11, 30, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void ScheduledTask_NextExecution_should_be_in_utc()
    {
        var task = new ScheduledTask
        {
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-5)
        };

        Assert.That(task.NextExecution.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(task.NextExecution, Is.GreaterThan(DateTime.UtcNow));
    }
}
