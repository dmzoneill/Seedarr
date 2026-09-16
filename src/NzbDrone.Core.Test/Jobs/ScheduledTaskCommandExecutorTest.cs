using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Test.Jobs;

[TestFixture]
public class ScheduledTaskCommandExecutorTest
{
    private ITaskManager _taskManager;
    private IScheduledTask _fakeTask;
    private ScheduledTaskCommandExecutor _subject;

    private class SampleTask : IScheduledTask
    {
        public int DefaultInterval => 15;
        public bool Executed { get; set; }

        public void Execute()
        {
            Executed = true;
        }
    }

    [SetUp]
    public void SetUp()
    {
        _taskManager = Substitute.For<ITaskManager>();
        _fakeTask = new SampleTask();
        _subject = new ScheduledTaskCommandExecutor(new[] { _fakeTask }, _taskManager);
    }

    [Test]
    public void Execute_should_run_task_instance_and_update_task_manager()
    {
        var sampleTask = (SampleTask)_fakeTask;
        var command = new ScheduledTaskCommand { TaskName = typeof(SampleTask).FullName };

        _subject.Execute(command);

        Assert.That(sampleTask.Executed, Is.True);
        _taskManager.Received(1).RecordTaskStarted(typeof(SampleTask).FullName);
        _taskManager.Received(1).UpdateLastExecution(typeof(SampleTask).FullName);
        _taskManager.Received(1).RecordTaskFinished(typeof(SampleTask).FullName, Arg.Any<DateTime>());
    }

    [Test]
    public void Execute_should_throw_when_task_name_empty()
    {
        var command = new ScheduledTaskCommand { TaskName = "" };

        Assert.Throws<ArgumentException>(() => _subject.Execute(command));
    }

    [Test]
    public void Execute_should_throw_when_task_implementation_not_found()
    {
        var command = new ScheduledTaskCommand { TaskName = "NonExistentTask" };

        Assert.Throws<InvalidOperationException>(() => _subject.Execute(command));
    }

    [Test]
    public void Execute_should_throw_when_task_is_already_running()
    {
        _taskManager.IsRunning(typeof(SampleTask).FullName).Returns(true);
        var command = new ScheduledTaskCommand { TaskName = typeof(SampleTask).FullName };

        Assert.Throws<InvalidOperationException>(() => _subject.Execute(command));
    }
}
