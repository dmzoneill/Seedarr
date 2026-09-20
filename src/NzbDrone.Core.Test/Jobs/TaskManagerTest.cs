using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;

namespace NzbDrone.Core.Test.Jobs;

[TestFixture]
public class TaskManagerTest
{
    private IBasicRepository<ScheduledTask> _repository;
    private TaskManager _subject;

    private class FakeScheduledTask : IScheduledTask
    {
        public int DefaultInterval => 15;

        public void Execute()
        {
        }
    }

    private class AnotherScheduledTask : IScheduledTask
    {
        public int DefaultInterval => 30;

        public void Execute()
        {
        }
    }

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IBasicRepository<ScheduledTask>>();
        _repository.All().Returns(new List<ScheduledTask>());
    }

    [Test]
    public void GetAll_should_return_all_tasks()
    {
        var tasks = new List<ScheduledTask>
        {
            new() { Id = 1, TypeName = "Task1", Interval = 10, LastExecution = DateTime.UtcNow },
            new() { Id = 2, TypeName = "Task2", Interval = 20, LastExecution = DateTime.UtcNow }
        };
        _repository.All().Returns(tasks);
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        var result = _subject.GetAll();

        Assert.That(result.Count(), Is.EqualTo(2));
    }

    [Test]
    public void GetNextScheduled_should_return_task_due_soonest()
    {
        var now = DateTime.UtcNow;
        var tasks = new List<ScheduledTask>
        {
            new() { Id = 1, TypeName = "Later", Interval = 60, LastExecution = now },
            new() { Id = 2, TypeName = "Sooner", Interval = 5, LastExecution = now.AddMinutes(-10) }
        };
        _repository.All().Returns(tasks);
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        var result = _subject.GetNextScheduled();

        Assert.That(result.TypeName, Is.EqualTo("Sooner"));
    }

    [Test]
    public void GetNextScheduled_should_return_null_when_no_tasks()
    {
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        var result = _subject.GetNextScheduled();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void UpdateLastExecution_should_update_matching_task()
    {
        var task = new ScheduledTask { Id = 1, TypeName = "MyTask", Interval = 10, LastExecution = DateTime.UtcNow.AddHours(-1) };
        _repository.All().Returns(new List<ScheduledTask> { task });
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        _subject.UpdateLastExecution("MyTask");

        _repository.Received(1).Update(Arg.Is<ScheduledTask>(t => t.TypeName == "MyTask"));
    }

    [Test]
    public void UpdateLastExecution_should_do_nothing_when_no_match()
    {
        _repository.All().Returns(new List<ScheduledTask>());
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        _subject.UpdateLastExecution("NonExistent");

        _repository.DidNotReceive().Update(Arg.Any<ScheduledTask>());
    }

    [Test]
    public void UpdateLastExecution_should_be_case_insensitive()
    {
        var task = new ScheduledTask { Id = 1, TypeName = "MyTask", Interval = 10, LastExecution = DateTime.UtcNow.AddHours(-1) };
        _repository.All().Returns(new List<ScheduledTask> { task });
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        _subject.UpdateLastExecution("mytask");

        _repository.Received(1).Update(Arg.Any<ScheduledTask>());
    }

    [Test]
    public void Handle_should_register_new_tasks()
    {
        _repository.All().Returns(new List<ScheduledTask>());
        var scheduledTasks = new List<IScheduledTask> { new FakeScheduledTask() };
        _subject = new TaskManager(_repository, scheduledTasks);

        _subject.Handle(new ApplicationStartedEvent());

        _repository.Received(1).Insert(Arg.Is<ScheduledTask>(t =>
            t.TypeName == typeof(FakeScheduledTask).FullName && t.Interval == 15));
    }

    [Test]
    public void Handle_should_not_re_register_existing_tasks()
    {
        var existing = new ScheduledTask
        {
            Id = 1,
            TypeName = typeof(FakeScheduledTask).FullName,
            Interval = 15,
            LastExecution = DateTime.UtcNow
        };
        _repository.All().Returns(new List<ScheduledTask> { existing });
        var scheduledTasks = new List<IScheduledTask> { new FakeScheduledTask() };
        _subject = new TaskManager(_repository, scheduledTasks);

        _subject.Handle(new ApplicationStartedEvent());

        _repository.DidNotReceive().Insert(Arg.Any<ScheduledTask>());
    }

    [Test]
    public void Handle_should_register_only_missing_tasks()
    {
        var existing = new ScheduledTask
        {
            Id = 1,
            TypeName = typeof(FakeScheduledTask).FullName,
            Interval = 15,
            LastExecution = DateTime.UtcNow
        };
        _repository.All().Returns(new List<ScheduledTask> { existing });
        var scheduledTasks = new List<IScheduledTask> { new FakeScheduledTask(), new AnotherScheduledTask() };
        _subject = new TaskManager(_repository, scheduledTasks);

        _subject.Handle(new ApplicationStartedEvent());

        _repository.Received(1).Insert(Arg.Is<ScheduledTask>(t =>
            t.TypeName == typeof(AnotherScheduledTask).FullName));
    }

    [Test]
    public void RecordTaskStarted_should_track_running_and_update_last_start_time()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-20)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        Assert.That(_subject.IsRunning("TestTask"), Is.False);

        _subject.RecordTaskStarted("TestTask");

        Assert.That(_subject.IsRunning("TestTask"), Is.True);
        _repository.Received(1).Update(Arg.Is<ScheduledTask>(t => t.LastStartTime.HasValue));
    }

    [Test]
    public void RecordTaskStarted_should_throw_when_task_already_running()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-20)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        _subject.RecordTaskStarted("TestTask");

        Assert.Throws<InvalidOperationException>(() => _subject.RecordTaskStarted("TestTask"));
    }

    [Test]
    public void RecordTaskFinished_should_clear_running_and_update_last_execution()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-20)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        var startTime = DateTime.UtcNow;
        _subject.RecordTaskStarted("TestTask");
        Assert.That(_subject.IsRunning("TestTask"), Is.True);

        _subject.RecordTaskFinished("TestTask", startTime);
        Assert.That(_subject.IsRunning("TestTask"), Is.False);

        _repository.Received().Update(Arg.Is<ScheduledTask>(t =>
            t.LastStartTime == startTime && t.LastExecution >= startTime));
    }

    [Test]
    public void Handle_should_reset_stale_in_flight_tasks()
    {
        var staleTask = new ScheduledTask
        {
            Id = 1,
            TypeName = typeof(FakeScheduledTask).FullName,
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddHours(-2),
            LastStartTime = DateTime.UtcNow.AddHours(-1) // LastStartTime > LastExecution
        };
        _repository.All().Returns(new List<ScheduledTask> { staleTask });
        var scheduledTasks = new List<IScheduledTask> { new FakeScheduledTask() };
        _subject = new TaskManager(_repository, scheduledTasks);

        _subject.Handle(new ApplicationStartedEvent());

        _repository.Received().Update(Arg.Is<ScheduledTask>(t =>
            t.LastStartTime == t.LastExecution));
    }

    [Test]
    public void CancelTask_by_id_should_cancel_running_task_and_update_status()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-20)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        using var cts = new CancellationTokenSource();
        _subject.RecordTaskStarted("TestTask", cts);

        var result = _subject.CancelTask(1);

        Assert.That(result, Is.True);
        Assert.That(cts.IsCancellationRequested, Is.True);
        Assert.That(_subject.IsCanceled("TestTask"), Is.True);
        Assert.That(_subject.GetTaskStatus("TestTask"), Is.EqualTo("Canceled"));
    }

    [Test]
    public void CancelTask_by_id_should_return_false_when_task_not_found()
    {
        _repository.All().Returns(new List<ScheduledTask>());
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        var result = _subject.CancelTask(999);

        Assert.That(result, Is.False);
    }

    [Test]
    public void CancelTask_by_name_should_cancel_running_task_matching_short_name()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "NzbDrone.Core.Jobs.CleanupTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-20)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        using var cts = new CancellationTokenSource();
        _subject.RecordTaskStarted("NzbDrone.Core.Jobs.CleanupTask", cts);

        var result = _subject.CancelTask("CleanupTask");

        Assert.That(result, Is.True);
        Assert.That(cts.IsCancellationRequested, Is.True);
        Assert.That(_subject.IsCanceled("NzbDrone.Core.Jobs.CleanupTask"), Is.True);
    }

    [Test]
    public void CancelTask_by_name_should_return_false_when_task_not_running()
    {
        _repository.All().Returns(new List<ScheduledTask>());
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        var result = _subject.CancelTask("NonExistentTask");

        Assert.That(result, Is.False);
    }

    [Test]
    public void RecordTaskFinished_should_preserve_canceled_status()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-20)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        using var cts = new CancellationTokenSource();
        _subject.RecordTaskStarted("TestTask", cts);
        _subject.CancelTask(1);

        var startTime = DateTime.UtcNow;
        _subject.RecordTaskFinished("TestTask", startTime);

        Assert.That(_subject.IsRunning("TestTask"), Is.False);
        Assert.That(_subject.IsCanceled("TestTask"), Is.True);
        Assert.That(_subject.GetTaskStatus("TestTask"), Is.EqualTo("Canceled"));
    }

    [Test]
    public void GetNextExecution_should_return_utc_next_execution()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-5)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        var next = _subject.GetNextExecution("TestTask");

        Assert.That(next.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(next, Is.GreaterThan(DateTime.UtcNow));
    }

    [Test]
    public void RecordTaskFinished_should_save_history_record_with_success_and_duration()
    {
        var task = new ScheduledTask
        {
            Id = 42,
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-20)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        var historyRepo = Substitute.For<IScheduledTaskHistoryRepository>();
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>(), historyRepo);

        var startTime = DateTime.UtcNow.AddSeconds(-3);
        _subject.RecordTaskStarted("TestTask", ScheduledTaskTriggerSource.Manual);
        _subject.RecordTaskFinished("TestTask", startTime);

        historyRepo.Received(1).Insert(Arg.Is<ScheduledTaskHistory>(h =>
            h.TaskId == 42 &&
            h.TypeName == "TestTask" &&
            h.Status == ScheduledTaskHistoryStatus.Success &&
            h.TriggerSource == ScheduledTaskTriggerSource.Manual &&
            h.DurationMs >= 2000));
    }

    [Test]
    public void RecordTaskFailed_should_save_history_record_with_failed_status_and_error_message()
    {
        var task = new ScheduledTask
        {
            Id = 42,
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-20)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        var historyRepo = Substitute.For<IScheduledTaskHistoryRepository>();
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>(), historyRepo);

        var startTime = DateTime.UtcNow.AddSeconds(-2);
        _subject.RecordTaskStarted("TestTask", ScheduledTaskTriggerSource.Scheduler);
        _subject.RecordTaskFailed("TestTask", startTime, new InvalidOperationException("Simulated failure"));

        historyRepo.Received(1).Insert(Arg.Is<ScheduledTaskHistory>(h =>
            h.TaskId == 42 &&
            h.TypeName == "TestTask" &&
            h.Status == ScheduledTaskHistoryStatus.Failed &&
            h.TriggerSource == ScheduledTaskTriggerSource.Scheduler &&
            h.ErrorMessage == "Simulated failure" &&
            h.ExceptionDetails.Contains("Simulated failure")));

        // Calling RecordTaskFinished afterwards does not insert a duplicate
        _subject.RecordTaskFinished("TestTask", startTime);
        historyRepo.Received(1).Insert(Arg.Any<ScheduledTaskHistory>());
    }

    [Test]
    public void RecordTaskFinished_after_cancel_should_save_history_record_with_canceled_status()
    {
        var task = new ScheduledTask
        {
            Id = 42,
            TypeName = "TestTask",
            Interval = 15,
            LastExecution = DateTime.UtcNow.AddMinutes(-20)
        };
        _repository.All().Returns(new List<ScheduledTask> { task });
        var historyRepo = Substitute.For<IScheduledTaskHistoryRepository>();
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>(), historyRepo);

        var startTime = DateTime.UtcNow.AddSeconds(-1);
        using var cts = new CancellationTokenSource();
        _subject.RecordTaskStarted("TestTask", cts, ScheduledTaskTriggerSource.Api);
        _subject.CancelTask(42);
        _subject.RecordTaskFinished("TestTask", startTime);

        historyRepo.Received(1).Insert(Arg.Is<ScheduledTaskHistory>(h =>
            h.TaskId == 42 &&
            h.TypeName == "TestTask" &&
            h.Status == ScheduledTaskHistoryStatus.Canceled &&
            h.TriggerSource == ScheduledTaskTriggerSource.Api));
    }

    [Test]
    public void GetTaskHistory_should_delegate_to_history_repository()
    {
        var historyRepo = Substitute.For<IScheduledTaskHistoryRepository>();
        var items = new List<ScheduledTaskHistory>
        {
            new() { Id = 1, TaskId = 10, TypeName = "TaskA", Status = ScheduledTaskHistoryStatus.Success }
        };
        historyRepo.GetByTaskId(10, 20).Returns(items);
        historyRepo.GetByTypeName("TaskA", 20).Returns(items);

        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>(), historyRepo);

        var byId = _subject.GetTaskHistory(10, 20);
        var byName = _subject.GetTaskHistory("TaskA", 20);

        Assert.That(byId, Is.EqualTo(items));
        Assert.That(byName, Is.EqualTo(items));
    }

    [Test]
    public void Update_should_update_interval_and_enabled_state()
    {
        var task = new ScheduledTask
        {
            Id = 5,
            TypeName = "TestTask",
            Interval = 15,
            IsEnabled = true,
            LastExecution = DateTime.UtcNow
        };
        _repository.Get(5).Returns(task);
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        _subject.Update(5, 45, false);

        Assert.That(task.Interval, Is.EqualTo(45));
        Assert.That(task.IsEnabled, Is.False);
        _repository.Received(1).Update(task);
    }

    [Test]
    public void Update_should_clamp_interval_to_minimum_one()
    {
        var task = new ScheduledTask
        {
            Id = 5,
            TypeName = "TestTask",
            Interval = 15,
            IsEnabled = true,
            LastExecution = DateTime.UtcNow
        };
        _repository.Get(5).Returns(task);
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        _subject.Update(5, 0, true);

        Assert.That(task.Interval, Is.EqualTo(1));
        Assert.That(task.IsEnabled, Is.True);
        _repository.Received(1).Update(task);
    }

    [Test]
    public void Update_should_do_nothing_when_task_not_found()
    {
        _repository.Get(99).Returns((ScheduledTask)null);
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        _subject.Update(99, 30, true);

        _repository.DidNotReceive().Update(Arg.Any<ScheduledTask>());
    }

    [Test]
    public void GetNextScheduled_should_ignore_disabled_tasks()
    {
        var now = DateTime.UtcNow;
        var tasks = new List<ScheduledTask>
        {
            new() { Id = 1, TypeName = "DisabledSooner", Interval = 5, IsEnabled = false, LastExecution = now.AddMinutes(-30) },
            new() { Id = 2, TypeName = "EnabledLater", Interval = 15, IsEnabled = true, LastExecution = now.AddMinutes(-10) }
        };
        _repository.All().Returns(tasks);
        _subject = new TaskManager(_repository, Enumerable.Empty<IScheduledTask>());

        var result = _subject.GetNextScheduled();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.TypeName, Is.EqualTo("EnabledLater"));
    }
}
