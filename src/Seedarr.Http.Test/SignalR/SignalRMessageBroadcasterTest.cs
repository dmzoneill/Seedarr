using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
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
        _hubClients.Group(Arg.Any<string>()).Returns(_clientProxy);
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

    [Test]
    public void BroadcastMessage_broadcasts_trackerUpdated_and_trackerAnnounced_events()
    {
        var updatedMsg = new SignalRMessage
        {
            Name = "TrackerUpdated",
            Action = ModelAction.Updated,
            Body = new { TorrentId = 1, TrackerId = 2, Url = "http://tracker.org/announce", Status = "Working" }
        };

        var announcedMsg = new SignalRMessage
        {
            Name = "TrackerAnnounced",
            Action = ModelAction.Updated,
            Body = new { TorrentId = 1, TrackerId = 2, Url = "http://tracker.org/announce", Status = "Working" }
        };

        _broadcaster.BroadcastMessage(updatedMsg);
        _broadcaster.BroadcastMessage(announcedMsg);

        _clientProxy.Received(1).SendCoreAsync("trackerUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("trackerAnnounced", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void TrackerSignalREventHandler_handles_TrackerAnnounceEvent_and_broadcasts()
    {
        var handler = new TrackerSignalREventHandler(_hubContext);
        var torrent = new Torrent { Id = 10, Name = "Test Torrent" };
        var announceEvent = new TrackerAnnounceEvent(torrent, "http://tr.com/announce", 25, 5, 30, 150, true, null, 7, TrackerStatus.Working);

        handler.Handle(announceEvent);

        _clientProxy.Received(1).SendCoreAsync("trackerUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("trackerAnnounced", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void TrackerSignalREventHandler_handles_TrackerStatusChangedEvent_and_broadcasts()
    {
        var handler = new TrackerSignalREventHandler(_hubContext);
        var torrent = new Torrent { Id = 11, Name = "Test Torrent 2" };
        var tracker = new TrackerEntry { Id = 8, TorrentId = 11, Url = "http://tr.com/announce", Status = TrackerStatus.Announcing };
        var statusEvent = new TrackerStatusChangedEvent(torrent, tracker, TrackerStatus.Unknown, TrackerStatus.Announcing);

        handler.Handle(statusEvent);

        _clientProxy.Received(1).SendCoreAsync("trackerUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("trackerAnnounced", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastToGroup_routes_to_specified_group_and_sends_receiveMessage_and_named_event()
    {
        var groupProxy = Substitute.For<IClientProxy>();
        groupProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _hubClients.Group("custom-group").Returns(groupProxy);

        var msg = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 5, Name = "Test" }
        };

        _broadcaster.BroadcastToGroup("custom-group", msg);

        _hubClients.Received(1).Group("custom-group");
        groupProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Is<object[]>(args => args.Length == 1 && args[0] == msg), Arg.Any<CancellationToken>());
        groupProxy.Received(1).SendCoreAsync("TorrentUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _ = _hubClients.DidNotReceive().All;
    }

    [Test]
    public void BroadcastToGroup_deduplicates_identical_payloads_within_window()
    {
        var groupProxy = Substitute.For<IClientProxy>();
        groupProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _hubClients.Group("group-1").Returns(groupProxy);

        var msg1 = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 10, Progress = 80.0 }
        };
        var msg2 = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 10, Progress = 80.0 }
        };

        _broadcaster.BroadcastToGroup("group-1", msg1);
        _broadcaster.BroadcastToGroup("group-1", msg2);

        groupProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        groupProxy.Received(1).SendCoreAsync("TorrentUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastToTorrent_routes_to_torrent_group()
    {
        var torrentProxy = Substitute.For<IClientProxy>();
        torrentProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _hubClients.Group("torrent-42").Returns(torrentProxy);

        var msg = new SignalRMessage
        {
            Name = "PieceCompleted",
            Action = ModelAction.Updated,
            Body = new { TorrentId = 42, PieceIndex = 3 }
        };

        _broadcaster.BroadcastToTorrent(42, msg);

        _hubClients.Received(1).Group("torrent-42");
        torrentProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        torrentProxy.Received(1).SendCoreAsync("PieceCompleted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastToChannel_routes_to_lowercased_channel_group()
    {
        var channelProxy = Substitute.For<IClientProxy>();
        channelProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _hubClients.Group("channel-alerts").Returns(channelProxy);

        var msg = new SignalRMessage
        {
            Name = "HealthCheckCompleted",
            Action = ModelAction.Updated,
            Body = new { Status = "Ok" }
        };

        _broadcaster.BroadcastToChannel("Alerts", msg);

        _hubClients.Received(1).Group("channel-alerts");
        channelProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        channelProxy.Received(1).SendCoreAsync("HealthCheckCompleted", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastToGroup_does_not_throw_when_group_or_message_is_null()
    {
        Assert.DoesNotThrow(() => _broadcaster.BroadcastToGroup(null, new SignalRMessage { Name = "Test" }));
        Assert.DoesNotThrow(() => _broadcaster.BroadcastToGroup("test-group", null));
        Assert.DoesNotThrow(() => _broadcaster.BroadcastToChannel(null, new SignalRMessage { Name = "Test" }));
        _hubClients.DidNotReceive().Group(Arg.Any<string>());
    }

    [Test]
    public async Task MessageHub_SubscribeToTorrent_and_UnsubscribeFromTorrent_manage_groups()
    {
        var hub = new MessageHub();
        var callerContext = Substitute.For<HubCallerContext>();
        var groupManager = Substitute.For<IGroupManager>();

        callerContext.ConnectionId.Returns("conn-42");
        hub.Context = callerContext;
        hub.Groups = groupManager;

        await hub.SubscribeToTorrent(123);
        await groupManager.Received(1).AddToGroupAsync("conn-42", "torrent-123", Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromTorrent(123);
        await groupManager.Received(1).RemoveFromGroupAsync("conn-42", "torrent-123", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MessageHub_SubscribeToChannel_and_UnsubscribeFromChannel_manage_groups_lowercased()
    {
        var hub = new MessageHub();
        var callerContext = Substitute.For<HubCallerContext>();
        var groupManager = Substitute.For<IGroupManager>();

        callerContext.ConnectionId.Returns("conn-42");
        hub.Context = callerContext;
        hub.Groups = groupManager;

        await hub.SubscribeToChannel("SystemStats");
        await groupManager.Received(1).AddToGroupAsync("conn-42", "channel-systemstats", Arg.Any<CancellationToken>());

        await hub.UnsubscribeFromChannel("SystemStats");
        await groupManager.Received(1).RemoveFromGroupAsync("conn-42", "channel-systemstats", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MessageHub_SubscribeToChannel_ignores_null_or_whitespace()
    {
        var hub = new MessageHub();
        var callerContext = Substitute.For<HubCallerContext>();
        var groupManager = Substitute.For<IGroupManager>();

        callerContext.ConnectionId.Returns("conn-42");
        hub.Context = callerContext;
        hub.Groups = groupManager;

        await hub.SubscribeToChannel(null);
        await hub.SubscribeToChannel("   ");
        await hub.UnsubscribeFromChannel(null);
        await hub.UnsubscribeFromChannel("   ");

        await groupManager.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await groupManager.DidNotReceive().RemoveFromGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
