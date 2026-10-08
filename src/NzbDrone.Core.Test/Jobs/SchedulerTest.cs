using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Jobs;
using NzbDrone.SignalR;

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

        public void Execute(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCount++;
        }
    }

    private class ThrowingScheduledTask : IScheduledTask
    {
        public int DefaultInterval => 1;

        public void Execute(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Task failed");
        }
    }

    private class CancelingScheduledTask : IScheduledTask
    {
        public int DefaultInterval => 1;

        public void Execute(CancellationToken cancellationToken)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static string GetBodyTypeName(object body)
    {
        return body?.GetType().GetProperty("TypeName")?.GetValue(body) as string;
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
        _subject = new Scheduler(_taskManager, new List<IScheduledTask>(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

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
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { testTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        Assert.That(testTask.ExecuteCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_should_broadcast_normalized_type_name_when_db_has_short_name()
    {
        var testTask = new TestScheduledTask();
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var scheduled = new ScheduledTask
        {
            TypeName = nameof(TestScheduledTask),
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled, (ScheduledTask)null);
        _subject = new Scheduler(
            _taskManager,
            new List<IScheduledTask> { testTask },
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(10),
            broadcaster);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        var expectedTypeName = typeof(TestScheduledTask).FullName;
        broadcaster.Received().BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "TaskStarted" && GetBodyTypeName(m.Body) == expectedTypeName));
        broadcaster.Received().BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "TaskCompleted" && GetBodyTypeName(m.Body) == expectedTypeName));
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
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { testTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

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
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { throwingTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        _taskManager.Received().RecordTaskFinished(typeof(ThrowingScheduledTask).FullName, Arg.Any<DateTime>(), ScheduledTaskTriggerSource.Scheduler);
        _taskManager.DidNotReceive().UpdateLastExecution(Arg.Any<string>());
        _taskManager.Received().RecordTaskFailed(
            typeof(ThrowingScheduledTask).FullName,
            Arg.Any<DateTime>(),
            Arg.Is<Exception>(ex => ex.Message == "Task failed"),
            ScheduledTaskTriggerSource.Scheduler);
    }

    [Test]
    public async Task ExecuteAsync_should_record_task_failed_when_task_is_canceled()
    {
        var cancelingTask = new CancelingScheduledTask();
        var scheduled = new ScheduledTask
        {
            TypeName = typeof(CancelingScheduledTask).FullName,
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled, (ScheduledTask)null);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { cancelingTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        _taskManager.Received().RecordTaskFailed(
            typeof(CancelingScheduledTask).FullName,
            Arg.Any<DateTime>(),
            Arg.Any<OperationCanceledException>(),
            ScheduledTaskTriggerSource.Scheduler);
        _taskManager.Received().RecordTaskFinished(typeof(CancelingScheduledTask).FullName, Arg.Any<DateTime>(), ScheduledTaskTriggerSource.Scheduler);
    }

    [Test]
    public async Task ExecuteAsync_should_broadcast_TaskFailed_not_TaskCompleted_when_task_is_canceled()
    {
        var cancelingTask = new CancelingScheduledTask();
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var scheduled = new ScheduledTask
        {
            TypeName = typeof(CancelingScheduledTask).FullName,
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled, (ScheduledTask)null);
        _subject = new Scheduler(
            _taskManager,
            new List<IScheduledTask> { cancelingTask },
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(10),
            broadcaster);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TaskStarted"));
        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TaskFailed"));
        broadcaster.DidNotReceive().BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TaskCompleted"));
    }

    [Test]
    public async Task ExecuteAsync_should_broadcast_TaskFailed_not_TaskCompleted_when_task_instance_missing()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var scheduled = new ScheduledTask
        {
            TypeName = "NonExistent.TaskType",
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled, (ScheduledTask)null);
        _subject = new Scheduler(
            _taskManager,
            new List<IScheduledTask>(),
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(10),
            broadcaster);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TaskStarted"));
        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TaskFailed"));
        broadcaster.DidNotReceive().BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TaskCompleted"));
    }

    [Test]
    public async Task ExecuteAsync_should_record_task_finished_when_no_task_instance_found()
    {
        var scheduled = new ScheduledTask
        {
            TypeName = "NonExistent.TaskType",
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled, (ScheduledTask)null);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask>(), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        _taskManager.Received().RecordTaskFailed(
            "NonExistent.TaskType",
            Arg.Any<DateTime>(),
            "No task instance found for scheduled type: NonExistent.TaskType",
            null,
            ScheduledTaskTriggerSource.Scheduler);
        _taskManager.Received().RecordTaskFinished("NonExistent.TaskType", Arg.Any<DateTime>(), ScheduledTaskTriggerSource.Scheduler);
        _taskManager.DidNotReceive().UpdateLastExecution(Arg.Any<string>());
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
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { validTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        _taskManager.Received().RecordTaskFailed(
            "NonExistent.TaskType",
            Arg.Any<DateTime>(),
            "No task instance found for scheduled type: NonExistent.TaskType",
            null,
            ScheduledTaskTriggerSource.Scheduler);
        _taskManager.Received().RecordTaskFinished("NonExistent.TaskType", Arg.Any<DateTime>(), ScheduledTaskTriggerSource.Scheduler);
        _taskManager.DidNotReceive().UpdateLastExecution(Arg.Any<string>());
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
            maxJitter: TimeSpan.Zero,
            tickDelay: TimeSpan.FromMilliseconds(10));

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
    public void IsSchedulerDue_should_align_with_calculate_next_execution_after_missed_intervals()
    {
        var baseTime = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
        var beforeSlot = baseTime.AddMinutes(65);
        var atSlot = baseTime.AddMinutes(90);

        Assert.That(ScheduledTask.IsSchedulerDue(baseTime, 30, beforeSlot), Is.False);
        Assert.That(ScheduledTask.IsSchedulerDue(baseTime, 30, atSlot), Is.True);
    }

    [Test]
    public async Task ExecuteAsync_should_not_execute_missed_interval_task_before_drift_free_next_execution()
    {
        var testTask = new TestScheduledTask();
        var scheduled = new ScheduledTask
        {
            TypeName = typeof(TestScheduledTask).FullName,
            Interval = 30,
            LastExecution = DateTime.UtcNow.AddMinutes(-65)
        };

        _taskManager.GetNextScheduled().Returns(scheduled);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { testTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        Assert.That(testTask.ExecuteCount, Is.EqualTo(0));
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

    [Test]
    public async Task ExecuteAsync_should_not_execute_disabled_task()
    {
        var testTask = new TestScheduledTask();
        var scheduled = new ScheduledTask
        {
            TypeName = typeof(TestScheduledTask).FullName,
            Interval = 1,
            IsEnabled = false,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { testTask }, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(200);

        Assert.That(testTask.ExecuteCount, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_should_default_tick_delay_to_30_seconds()
    {
        var scheduler = new Scheduler(_taskManager, new List<IScheduledTask>());
        Assert.That(scheduler.TickDelay, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void Constructor_with_jitter_parameters_should_default_tick_delay_to_30_seconds()
    {
        var scheduler = new Scheduler(
            _taskManager,
            new List<IScheduledTask>(),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15));

        Assert.That(scheduler.TickDelay, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void Constructor_with_custom_tick_delay_should_set_tick_delay()
    {
        var scheduler = new Scheduler(
            _taskManager,
            new List<IScheduledTask>(),
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(5));

        Assert.That(scheduler.TickDelay, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void Constructor_with_tick_delay_overload_should_set_tick_delay()
    {
        var scheduler = new Scheduler(
            _taskManager,
            new List<IScheduledTask>(),
            TimeSpan.FromSeconds(10));

        Assert.That(scheduler.TickDelay, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void TickDelay_can_be_set_directly()
    {
        var scheduler = new Scheduler(_taskManager, new List<IScheduledTask>())
        {
            TickDelay = TimeSpan.FromMilliseconds(50)
        };

        Assert.That(scheduler.TickDelay, Is.EqualTo(TimeSpan.FromMilliseconds(50)));
    }
}
