using System;
using System.Collections.Generic;
using System.Threading;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Jobs;
using NzbDrone.SignalR;

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

        public void Execute(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Executed = true;
        }
    }

    private class CancelingTask : IScheduledTask
    {
        public int DefaultInterval => 15;

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
        _taskManager.Received(1).RecordTaskStarted(
            typeof(SampleTask).FullName,
            Arg.Any<CancellationTokenSource>(),
            Arg.Any<TimeSpan?>(),
            Arg.Any<ScheduledTaskTriggerSource>());
        _taskManager.Received(1).UpdateLastExecution(typeof(SampleTask).FullName);
        _taskManager.Received(1).RecordTaskFinished(
            typeof(SampleTask).FullName,
            Arg.Any<DateTime>(),
            Arg.Any<ScheduledTaskTriggerSource?>());
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

    [Test]
    public void Execute_should_broadcast_TaskStarted_and_TaskCompleted()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var subject = new ScheduledTaskCommandExecutor(new[] { _fakeTask }, _taskManager, broadcaster);
        var command = new ScheduledTaskCommand { TaskName = typeof(SampleTask).FullName };

        subject.Execute(command);

        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "TaskStarted" &&
            m.Action == NzbDrone.Core.Datastore.ModelAction.Created));

        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "TaskCompleted" &&
            m.Action == NzbDrone.Core.Datastore.ModelAction.Updated));
    }

    [Test]
    public void Execute_should_broadcast_full_type_name_when_command_uses_short_name()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var subject = new ScheduledTaskCommandExecutor(new[] { _fakeTask }, _taskManager, broadcaster);
        var command = new ScheduledTaskCommand { TaskName = nameof(SampleTask) };

        subject.Execute(command);

        var expectedTypeName = typeof(SampleTask).FullName;
        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "TaskStarted" && GetBodyTypeName(m.Body) == expectedTypeName));
        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "TaskCompleted" && GetBodyTypeName(m.Body) == expectedTypeName));
    }

    [Test]
    public void Execute_should_rethrow_and_record_failure_when_task_is_canceled()
    {
        var cancelingTask = new CancelingTask();
        var subject = new ScheduledTaskCommandExecutor(new[] { cancelingTask }, _taskManager);
        var command = new ScheduledTaskCommand { TaskName = typeof(CancelingTask).FullName };

        Assert.Throws<OperationCanceledException>(() => subject.Execute(command));

        _taskManager.Received(1).RecordTaskFailed(
            typeof(CancelingTask).FullName,
            Arg.Any<DateTime>(),
            Arg.Any<OperationCanceledException>(),
            Arg.Any<ScheduledTaskTriggerSource?>());
    }

    [Test]
    public void Execute_should_broadcast_TaskFailed_not_TaskCompleted_when_canceled()
    {
        var cancelingTask = new CancelingTask();
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var subject = new ScheduledTaskCommandExecutor(new[] { cancelingTask }, _taskManager, broadcaster);
        var command = new ScheduledTaskCommand { TaskName = typeof(CancelingTask).FullName };

        Assert.Throws<OperationCanceledException>(() => subject.Execute(command));

        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TaskStarted"));
        broadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TaskFailed"));
        broadcaster.DidNotReceive().BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TaskCompleted"));
    }
}
