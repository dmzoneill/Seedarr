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
        _subject = new Scheduler(_taskManager, new List<IScheduledTask>());

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
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { testTask });

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
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { testTask });

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
        _subject = new Scheduler(_taskManager, new List<IScheduledTask> { throwingTask });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        _taskManager.Received().UpdateLastExecution(typeof(ThrowingScheduledTask).FullName);
    }

    [Test]
    public async Task ExecuteAsync_should_skip_when_no_task_instance_found()
    {
        var scheduled = new ScheduledTask
        {
            TypeName = "NonExistent.TaskType",
            Interval = 1,
            LastExecution = DateTime.UtcNow.AddMinutes(-10)
        };

        _taskManager.GetNextScheduled().Returns(scheduled, (ScheduledTask)null);
        _subject = new Scheduler(_taskManager, new List<IScheduledTask>());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _subject.StartAsync(cts.Token);
        await Task.Delay(500);

        _taskManager.DidNotReceive().UpdateLastExecution(Arg.Any<string>());
    }
}
