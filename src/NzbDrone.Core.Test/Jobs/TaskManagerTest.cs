using System;
using System.Collections.Generic;
using System.Linq;
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
}
