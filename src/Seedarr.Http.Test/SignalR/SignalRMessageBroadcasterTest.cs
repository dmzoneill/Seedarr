using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.SignalR;

namespace Seedarr.Http.Test.SignalR;

[TestFixture]
public class SignalRMessageBroadcasterTest
{
    private IHubContext<MessageHub> _hubContext;
    private IHubClients _hubClients;
    private IClientProxy _clientProxy;
    private SignalRMessageBroadcaster _broadcaster;

    [SetUp]
    public void SetUp()
    {
        _hubContext = Substitute.For<IHubContext<MessageHub>>();
        _hubClients = Substitute.For<IHubClients>();
        _clientProxy = Substitute.For<IClientProxy>();

        _hubContext.Clients.Returns(_hubClients);
        _hubClients.All.Returns(_clientProxy);
        _clientProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _broadcaster = new SignalRMessageBroadcaster(_hubContext, TimeSpan.FromMilliseconds(500));
    }

    [Test]
    public void BroadcastMessage_deduplicates_identical_payloads_within_window()
    {
        var msg1 = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 1, Progress = 50.0 }
        };

        var msg2 = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 1, Progress = 50.0 }
        };

        _broadcaster.BroadcastMessage(msg1);
        _broadcaster.BroadcastMessage(msg2);

        // receiveMessage should only be called once, because msg2 is an exact duplicate payload
        _clientProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("TorrentUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastMessage_sends_different_payloads()
    {
        var msg1 = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 1, Progress = 50.0 }
        };

        var msg2 = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 1, Progress = 51.0 }
        };

        _broadcaster.BroadcastMessage(msg1);
        _broadcaster.BroadcastMessage(msg2);

        _clientProxy.Received(2).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(2).SendCoreAsync("TorrentUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastMessage_does_not_throw_when_message_is_null()
    {
        Assert.DoesNotThrow(() => _broadcaster.BroadcastMessage(null));
        _clientProxy.DidNotReceive().SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastMessage_broadcasts_CommandStarted_and_CommandCompleted_events()
    {
        var startedMsg = new SignalRMessage
        {
            Name = "CommandStarted",
            Action = ModelAction.Created,
            Body = new { Id = 10, Name = "BackupCommand" }
        };

        var completedMsg = new SignalRMessage
        {
            Name = "CommandCompleted",
            Action = ModelAction.Updated,
            Body = new { Id = 10, Name = "BackupCommand" }
        };

        _broadcaster.BroadcastMessage(startedMsg);
        _broadcaster.BroadcastMessage(completedMsg);

        _clientProxy.Received(1).SendCoreAsync("CommandStarted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("CommandCompleted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastMessage_recognizes_Command_name_with_actions()
    {
        var startedMsg = new SignalRMessage
        {
            Name = "Command",
            Action = ModelAction.Created,
            Body = new { Id = 11 }
        };

        var completedMsg = new SignalRMessage
        {
            Name = "Command",
            Action = ModelAction.Updated,
            Body = new { Id = 11 }
        };

        _broadcaster.BroadcastMessage(startedMsg);
        _broadcaster.BroadcastMessage(completedMsg);

        _clientProxy.Received(1).SendCoreAsync("CommandStarted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("CommandCompleted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastMessage_broadcasts_TaskStarted_and_TaskCompleted_events()
    {
        var startedMsg = new SignalRMessage
        {
            Name = "TaskStarted",
            Action = ModelAction.Created,
            Body = new { TypeName = "CheckHealthTask" }
        };

        var completedMsg = new SignalRMessage
        {
            Name = "TaskCompleted",
            Action = ModelAction.Updated,
            Body = new { TypeName = "CheckHealthTask" }
        };

        _broadcaster.BroadcastMessage(startedMsg);
        _broadcaster.BroadcastMessage(completedMsg);

        _clientProxy.Received(1).SendCoreAsync("TaskStarted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("TaskCompleted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastMessage_recognizes_Task_name_with_actions()
    {
        var startedMsg = new SignalRMessage
        {
            Name = "Task",
            Action = ModelAction.Created,
            Body = new { TypeName = "BackupTask" }
        };

        var completedMsg = new SignalRMessage
        {
            Name = "Task",
            Action = ModelAction.Updated,
            Body = new { TypeName = "BackupTask" }
        };

        _broadcaster.BroadcastMessage(startedMsg);
        _broadcaster.BroadcastMessage(completedMsg);

        _clientProxy.Received(1).SendCoreAsync("TaskStarted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("TaskCompleted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }
}
