using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Controllers;

[TestFixture]
public class SystemControllerTasksTest
{
    private IAppFolderInfo _appFolderInfo;
    private IConfigService _configService;
    private IHostApplicationLifetime _lifetime;
    private ITaskManager _taskManager;
    private IScheduledTask _sampleTask;
    private IManageCommandQueue _commandQueueManager;
    private SystemController _controller;

    private class SampleScheduledTask : IScheduledTask
    {
        public int DefaultInterval => 15;
        public void Execute() { }
    }

    [SetUp]
    public void SetUp()
    {
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _configService = Substitute.For<IConfigService>();
        _lifetime = Substitute.For<IHostApplicationLifetime>();
        _taskManager = Substitute.For<ITaskManager>();
        _sampleTask = new SampleScheduledTask();
        _commandQueueManager = Substitute.For<IManageCommandQueue>();

        _controller = new SystemController(
            _taskManager,
            new[] { _sampleTask },
            _commandQueueManager,
            _appFolderInfo,
            _lifetime,
            _configService);
    }

    [Test]
    public void ExecuteTask_pushes_scheduled_task_command_to_queue()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = typeof(SampleScheduledTask).FullName,
            Interval = 15,
            LastExecution = DateTime.UtcNow
        };
        _taskManager.GetAll().Returns(new List<ScheduledTask> { task });
        _taskManager.IsRunning(task.TypeName).Returns(false);
        _commandQueueManager.Push(Arg.Any<ScheduledTaskCommand>(), CommandTrigger.Manual)
            .Returns(new CommandModel { Id = 10, Name = "ScheduledTaskCommand" });

        var result = _controller.ExecuteTask(1);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _commandQueueManager.Received(1).Push(
            Arg.Is<ScheduledTaskCommand>(c => c.TaskName == typeof(SampleScheduledTask).FullName),
            CommandTrigger.Manual);
    }

    [Test]
    public void ExecuteTask_returns_not_found_when_task_id_missing()
    {
        _taskManager.GetAll().Returns(new List<ScheduledTask>());

        var result = _controller.ExecuteTask(99);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void ExecuteTask_returns_not_found_when_task_implementation_missing()
    {
        var orphanedTask = new ScheduledTask
        {
            Id = 2,
            TypeName = "Missing.Task.Implementation",
            Interval = 15,
            LastExecution = DateTime.UtcNow
        };
        _taskManager.GetAll().Returns(new List<ScheduledTask> { orphanedTask });

        var result = _controller.ExecuteTask(2);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        _commandQueueManager.DidNotReceive().Push(Arg.Any<ScheduledTaskCommand>(), Arg.Any<CommandTrigger>());
    }

    [Test]
    public void ExecuteTask_returns_conflict_when_task_already_running()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = typeof(SampleScheduledTask).FullName,
            Interval = 15,
            LastExecution = DateTime.UtcNow
        };
        _taskManager.GetAll().Returns(new List<ScheduledTask> { task });
        _taskManager.IsRunning(task.TypeName).Returns(true);

        var result = _controller.ExecuteTask(1);

        Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
        _commandQueueManager.DidNotReceive().Push(Arg.Any<ScheduledTaskCommand>(), Arg.Any<CommandTrigger>());
    }

    [Test]
    public void ExecuteTaskByName_pushes_scheduled_task_command_to_queue()
    {
        var task = new ScheduledTask
        {
            Id = 1,
            TypeName = typeof(SampleScheduledTask).FullName,
            Interval = 15,
            LastExecution = DateTime.UtcNow
        };
        _taskManager.GetAll().Returns(new List<ScheduledTask> { task });
        _taskManager.IsRunning(task.TypeName).Returns(false);
        _commandQueueManager.Push(Arg.Any<ScheduledTaskCommand>(), CommandTrigger.Manual)
            .Returns(new CommandModel { Id = 11, Name = "ScheduledTaskCommand" });

        var result = _controller.ExecuteTaskByName(nameof(SampleScheduledTask));

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _commandQueueManager.Received(1).Push(
            Arg.Is<ScheduledTaskCommand>(c => c.TaskName == typeof(SampleScheduledTask).FullName),
            CommandTrigger.Manual);
    }

    [Test]
    public void AbortTask_returns_ok_when_task_cancelled_successfully()
    {
        _taskManager.CancelTask(1).Returns(true);

        var result = _controller.AbortTask(1);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _taskManager.Received(1).CancelTask(1);
    }

    [Test]
    public void AbortTask_returns_not_found_when_task_not_running_or_missing()
    {
        _taskManager.CancelTask(99).Returns(false);

        var result = _controller.AbortTask(99);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        _taskManager.Received(1).CancelTask(99);
    }

    [Test]
    public void AbortTaskByName_returns_ok_when_task_cancelled_successfully()
    {
        _taskManager.CancelTask("SampleScheduledTask").Returns(true);

        var result = _controller.AbortTaskByName("SampleScheduledTask");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _taskManager.Received(1).CancelTask("SampleScheduledTask");
    }

    [Test]
    public void AbortTaskByName_returns_not_found_when_task_not_running_or_missing()
    {
        _taskManager.CancelTask("UnknownTask").Returns(false);

        var result = _controller.AbortTaskByName("UnknownTask");

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
        _taskManager.Received(1).CancelTask("UnknownTask");
    }

    [Test]
    public void GetTasks_computes_running_duration_and_is_running_flag()
    {
        var now = DateTime.UtcNow;
        var runningTask = new ScheduledTask
        {
            Id = 1,
            TypeName = typeof(SampleScheduledTask).FullName,
            Interval = 15,
            LastExecution = now.AddMinutes(-30),
            LastStartTime = now.AddMinutes(-2) // LastStartTime > LastExecution indicates actively running
        };
        _taskManager.GetAll().Returns(new List<ScheduledTask> { runningTask });
        _taskManager.IsRunning(runningTask.TypeName).Returns(true);

        var actionResult = _controller.GetTasks();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var resources = okResult.Value as List<ScheduledTaskResource>;
        Assert.That(resources, Has.Count.EqualTo(1));
        var resource = resources[0];

        Assert.That(resource.IsRunning, Is.True);
        Assert.That(resource.LastDuration, Is.Not.Null);
        Assert.That(resource.LastDuration.Value.TotalSeconds, Is.GreaterThan(0));
    }

    [Test]
    public void CancelCommand_cancels_queued_or_started_command()
    {
        var cmd = new CommandModel { Id = 5, Name = "Sample", Status = CommandStatus.Queued };
        _commandQueueManager.Get(5).Returns(cmd);
        _commandQueueManager.Cancel(5).Returns(true);

        var result = _controller.CancelCommand(5);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _commandQueueManager.Received(1).Cancel(5);
    }

    [Test]
    public void CancelCommand_returns_not_found_when_command_missing()
    {
        _commandQueueManager.Get(99).Returns((CommandModel)null);

        var result = _controller.CancelCommand(99);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void CancelCommand_returns_bad_request_when_command_already_terminal()
    {
        var cmd = new CommandModel { Id = 5, Name = "Sample", Status = CommandStatus.Completed };
        _commandQueueManager.Get(5).Returns(cmd);

        var result = _controller.CancelCommand(5);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        _commandQueueManager.DidNotReceive().Cancel(5);
    }

    [Test]
    public void GetCommands_maps_scheduled_task_name_to_simple_name()
    {
        var commands = new List<CommandModel>
        {
            new()
            {
                Id = 1,
                Name = "ScheduledTaskCommand",
                Body = "{\"TaskName\":\"NzbDrone.Core.Housekeeping.CleanDatabase\"}",
                Status = CommandStatus.Queued,
                QueuedAt = DateTime.UtcNow
            }
        };
        _commandQueueManager.GetAll().Returns(commands);

        var actionResult = _controller.GetCommands();
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var resources = okResult.Value as List<CommandResource>;
        Assert.That(resources, Has.Count.EqualTo(1));
        Assert.That(resources[0].Name, Is.EqualTo("CleanDatabase"));
    }

    [Test]
    public void GetCommand_returns_command_when_found()
    {
        var model = new CommandModel
        {
            Id = 42,
            Name = "SyncArrCommand",
            Status = CommandStatus.Started,
            QueuedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
        };
        _commandQueueManager.Get(42).Returns(model);

        var actionResult = _controller.GetCommand(42);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var resource = okResult.Value as CommandResource;
        Assert.That(resource, Is.Not.Null);
        Assert.That(resource.Id, Is.EqualTo(42));
        Assert.That(resource.Name, Is.EqualTo("SyncArrCommand"));
        Assert.That(resource.Status, Is.EqualTo("started"));
    }

    [Test]
    public void GetCommand_returns_not_found_when_missing()
    {
        _commandQueueManager.Get(99).Returns((CommandModel)null);

        var actionResult = _controller.GetCommand(99);

        Assert.That(actionResult.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void PushCommand_returns_bad_request_when_payload_is_not_json_object()
    {
        using var doc = JsonDocument.Parse("[\"item1\", \"item2\"]");

        var actionResult = _controller.PushCommand(doc.RootElement);

        Assert.That(actionResult.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = actionResult.Result as BadRequestObjectResult;
        Assert.That(badRequest.Value, Is.EqualTo("Command payload must be a JSON object."));
    }

    [Test]
    public void PushCommand_returns_bad_request_when_name_is_missing()
    {
        using var doc = JsonDocument.Parse("{\"key\": \"value\"}");

        var actionResult = _controller.PushCommand(doc.RootElement);

        Assert.That(actionResult.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void PushCommand_pushes_raw_command_and_returns_resource()
    {
        using var doc = JsonDocument.Parse("{\"name\": \"SyncArr\"}");
        var model = new CommandModel
        {
            Id = 15,
            Name = "SyncArr",
            Status = CommandStatus.Queued,
            QueuedAt = DateTime.UtcNow
        };
        _commandQueueManager.PushRaw("SyncArr", Arg.Any<string>(), CommandTrigger.Manual).Returns(model);

        var actionResult = _controller.PushCommand(doc.RootElement);

        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var resource = okResult.Value as CommandResource;
        Assert.That(resource, Is.Not.Null);
        Assert.That(resource.Id, Is.EqualTo(15));
        Assert.That(resource.Name, Is.EqualTo("SyncArr"));
        Assert.That(resource.Status, Is.EqualTo("queued"));
    }

    [Test]
    public void GetTaskHistoryById_returns_ok_with_history_items()
    {
        var historyItems = new List<ScheduledTaskHistory>
        {
            new()
            {
                Id = 1,
                TaskId = 5,
                TypeName = "SampleScheduledTask",
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                FinishedAt = DateTime.UtcNow.AddMinutes(-4),
                DurationMs = 60000,
                Status = ScheduledTaskHistoryStatus.Success,
                TriggerSource = ScheduledTaskTriggerSource.Scheduler
            }
        };
        _taskManager.GetTaskHistory(5, 50).Returns(historyItems);

        var actionResult = _controller.GetTaskHistoryById(5);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var resources = okResult.Value as List<ScheduledTaskHistoryResource>;
        Assert.That(resources, Has.Count.EqualTo(1));
        Assert.That(resources[0].Id, Is.EqualTo(1));
        Assert.That(resources[0].TaskId, Is.EqualTo(5));
        Assert.That(resources[0].Status, Is.EqualTo("Success"));
        Assert.That(resources[0].TriggerSource, Is.EqualTo("Scheduler"));
        Assert.That(resources[0].DurationMs, Is.EqualTo(60000));
    }

    [Test]
    public void GetTaskHistoryByName_returns_ok_with_history_items()
    {
        var historyItems = new List<ScheduledTaskHistory>
        {
            new()
            {
                Id = 2,
                TaskId = 5,
                TypeName = "SampleScheduledTask",
                StartedAt = DateTime.UtcNow.AddMinutes(-2),
                FinishedAt = DateTime.UtcNow.AddMinutes(-1),
                DurationMs = 60000,
                Status = ScheduledTaskHistoryStatus.Failed,
                TriggerSource = ScheduledTaskTriggerSource.Manual,
                ErrorMessage = "Task error",
                ExceptionDetails = "Stack trace"
            }
        };
        _taskManager.GetTaskHistory("SampleScheduledTask", 20).Returns(historyItems);

        var actionResult = _controller.GetTaskHistoryByName("SampleScheduledTask", 20);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var resources = okResult.Value as List<ScheduledTaskHistoryResource>;
        Assert.That(resources, Has.Count.EqualTo(1));
        Assert.That(resources[0].Id, Is.EqualTo(2));
        Assert.That(resources[0].Status, Is.EqualTo("Failed"));
        Assert.That(resources[0].TriggerSource, Is.EqualTo("Manual"));
        Assert.That(resources[0].ErrorMessage, Is.EqualTo("Task error"));
    }
}
