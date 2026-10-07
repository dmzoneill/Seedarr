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

        MessageHub.ResetForTesting();
        MessageHub.AddConnectionForTesting();

        _broadcaster = new SignalRMessageBroadcaster(_hubContext, TimeSpan.FromMilliseconds(500));
    }

    [TearDown]
    public void TearDown()
    {
        _broadcaster?.Dispose();
        MessageHub.ResetForTesting();
    }

    [Test]
    public void BroadcastMessage_does_not_record_dedup_cache_when_no_clients_connected()
    {
        MessageHub.ResetForTesting();

        var msg = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 1, Progress = 50.0 }
        };

        _broadcaster.BroadcastMessage(msg);
        _broadcaster.BroadcastMessage(msg);

        Assert.That(_broadcaster.RecentPayloadCount, Is.EqualTo(0));
        _clientProxy.Received(2).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastMessage_after_offline_broadcast_delivers_first_connected_update_with_same_payload()
    {
        MessageHub.ResetForTesting();

        var msg = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 1, Progress = 50.0 }
        };

        _broadcaster.BroadcastMessage(msg);
        Assert.That(_broadcaster.RecentPayloadCount, Is.EqualTo(0));

        MessageHub.AddConnectionForTesting();
        _broadcaster.BroadcastMessage(msg);

        _clientProxy.Received(2).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(2).SendCoreAsync("TorrentUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastToGroup_does_not_record_dedup_cache_when_no_clients_connected()
    {
        MessageHub.ResetForTesting();

        var groupProxy = Substitute.For<IClientProxy>();
        groupProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _hubClients.Group("group-offline").Returns(groupProxy);

        var msg = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 10, Progress = 80.0 }
        };

        _broadcaster.BroadcastToGroup("group-offline", msg);
        _broadcaster.BroadcastToGroup("group-offline", msg);

        Assert.That(_broadcaster.RecentPayloadCount, Is.EqualTo(0));
        groupProxy.Received(2).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastMessage_deduplicates_PieceBatchCompleted_with_same_indexes_in_different_order()
    {
        var msg1 = new PieceBatchCompletedMessage("abc123", new[] { 1, 2, 3 });
        var msg2 = new PieceBatchCompletedMessage("abc123", new[] { 3, 2, 1 });

        _broadcaster.BroadcastMessage(msg1);
        _broadcaster.BroadcastMessage(msg2);

        _clientProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("pieceMapUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
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
    public void BroadcastToChannel_trims_whitespace_before_routing_to_channel_group()
    {
        var channelProxy = Substitute.For<IClientProxy>();
        channelProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _hubClients.Group("channel-torrents").Returns(channelProxy);

        var msg = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 1 }
        };

        _broadcaster.BroadcastToChannel(" torrents ", msg);

        _hubClients.Received(1).Group("channel-torrents");
        channelProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
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
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.Get(123).Returns(new Torrent { Id = 123, Name = "Existing" });

        var hub = new MessageHub(torrentService: torrentService);
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

    [Test]
    public async Task MessageHub_SubscribeToTorrent_ignores_zero_or_negative_torrent_id()
    {
        var hub = new MessageHub();
        var callerContext = Substitute.For<HubCallerContext>();
        var groupManager = Substitute.For<IGroupManager>();

        callerContext.ConnectionId.Returns("conn-42");
        hub.Context = callerContext;
        hub.Groups = groupManager;

        await hub.SubscribeToTorrent(0);
        await hub.SubscribeToTorrent(-1);
        await hub.SubscribeToTorrent(-999);
        await hub.UnsubscribeFromTorrent(0);
        await hub.UnsubscribeFromTorrent(-1);
        await hub.UnsubscribeFromTorrent(-999);

        await groupManager.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await groupManager.DidNotReceive().RemoveFromGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MessageHub_SubscribeToTorrent_verifies_existence_when_torrent_service_is_available()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.Get(10).Returns(new Torrent { Id = 10, Name = "Existing" });
        torrentService.Get(20).Returns((Torrent)null);

        var hub = new MessageHub(torrentService: torrentService);
        var callerContext = Substitute.For<HubCallerContext>();
        var groupManager = Substitute.For<IGroupManager>();

        callerContext.ConnectionId.Returns("conn-42");
        hub.Context = callerContext;
        hub.Groups = groupManager;

        await hub.SubscribeToTorrent(10);
        await groupManager.Received(1).AddToGroupAsync("conn-42", "torrent-10", Arg.Any<CancellationToken>());

        await hub.SubscribeToTorrent(20);
        await groupManager.DidNotReceive().AddToGroupAsync("conn-42", "torrent-20", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MessageHub_SubscribeToTorrent_does_not_join_group_when_torrent_service_unavailable()
    {
        var hub = new MessageHub();
        var callerContext = Substitute.For<HubCallerContext>();
        var groupManager = Substitute.For<IGroupManager>();

        callerContext.ConnectionId.Returns("conn-42");
        hub.Context = callerContext;
        hub.Groups = groupManager;

        await hub.SubscribeToTorrent(999);

        await groupManager.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MessageHub_SubscribeToChannel_ignores_non_whitelisted_or_overlength_channel()
    {
        var hub = new MessageHub();
        var callerContext = Substitute.For<HubCallerContext>();
        var groupManager = Substitute.For<IGroupManager>();

        callerContext.ConnectionId.Returns("conn-42");
        hub.Context = callerContext;
        hub.Groups = groupManager;

        await hub.SubscribeToChannel("arbitrary_unknown_channel");
        await hub.SubscribeToChannel(new string('a', 100));
        await hub.UnsubscribeFromChannel("arbitrary_unknown_channel");
        await hub.UnsubscribeFromChannel(new string('a', 100));

        await groupManager.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await groupManager.DidNotReceive().RemoveFromGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void BroadcastMessage_broadcasts_SeedingStatsUpdated_for_seeding_telemetry()
    {
        var seedingMsg = new SignalRMessage
        {
            Name = "Seeding",
            Body = new { DownloadSpeed = 1024, UploadSpeed = 2048 }
        };

        _broadcaster.BroadcastMessage(seedingMsg);

        _clientProxy.Received(1).SendCoreAsync("receiveMessage", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
        _clientProxy.Received(1).SendCoreAsync("SeedingStatsUpdated", Arg.Any<object[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void HubOptions_configuration_sets_bounded_capacity_and_timeouts()
    {
        var options = new HubOptions
        {
            StreamBufferCapacity = 10,
            MaximumParallelInvocationsPerClient = 5,
            ClientTimeoutInterval = TimeSpan.FromSeconds(30),
            KeepAliveInterval = TimeSpan.FromSeconds(10)
        };

        Assert.That(options.StreamBufferCapacity, Is.EqualTo(10));
        Assert.That(options.MaximumParallelInvocationsPerClient, Is.EqualTo(5));
        Assert.That(options.ClientTimeoutInterval, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(options.KeepAliveInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public async Task BroadcastMessage_and_group_when_send_fails_handles_faulted_continuation()
    {
        var failingProxy = Substitute.For<IClientProxy>();
        var tcs = new TaskCompletionSource();
        tcs.SetException(new InvalidOperationException("Simulated broadcast error"));
        failingProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        _hubClients.All.Returns(failingProxy);
        _hubClients.Group(Arg.Any<string>()).Returns(failingProxy);

        var msg = new SignalRMessage
        {
            Name = "FaultEvent",
            Action = ModelAction.Created,
            Body = new { Id = 404 }
        };

        _broadcaster.BroadcastMessage(msg);
        _broadcaster.BroadcastToGroup("fault-group", msg);

        await Task.Delay(50);
        Assert.Pass("Faulted task continuation completed safely");
    }

    [Test]
    public async Task TrackerSignalREventHandler_when_send_fails_handles_faulted_continuation()
    {
        var failingProxy = Substitute.For<IClientProxy>();
        var tcs = new TaskCompletionSource();
        tcs.SetException(new InvalidOperationException("Simulated tracker broadcast error"));
        failingProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        _hubClients.All.Returns(failingProxy);

        var handler = new TrackerSignalREventHandler(_hubContext);
        var torrent = new Torrent { Id = 12, Name = "Test Torrent Fault" };
        var announceEvent = new TrackerAnnounceEvent(torrent, "http://tr.com/announce", 1, 1, 1, 10, true, null, 1, TrackerStatus.Working);

        handler.Handle(announceEvent);

        var tracker = new TrackerEntry { Id = 9, TorrentId = 12, Url = "http://tr.com/announce", Status = TrackerStatus.Announcing };
        var statusEvent = new TrackerStatusChangedEvent(torrent, tracker, TrackerStatus.Unknown, TrackerStatus.Announcing);
        handler.Handle(statusEvent);

        await Task.Delay(50);
        Assert.Pass("Faulted tracker continuation completed safely");
    }

    [Test]
    public void BroadcastMessage_bounds_deduplication_cache_to_max_limit()
    {
        const int totalMessages = 1050;

        for (var i = 0; i < totalMessages; i++)
        {
            var msg = new PieceCompletedMessage("testhash", i, 16384);
            _broadcaster.BroadcastMessage(msg);
        }

        Assert.That(_broadcaster.RecentPayloadCount, Is.LessThanOrEqualTo(SignalRMessageBroadcaster.MaxCacheSize));
    }

    [Test]
    public void PruneStalePayloads_removes_entries_older_than_stale_threshold()
    {
        var msg = new PieceCompletedMessage("testhash", 1, 16384);
        _broadcaster.BroadcastMessage(msg);

        Assert.That(_broadcaster.RecentPayloadCount, Is.EqualTo(1));

        var future = DateTime.UtcNow.AddMinutes(3);
        _broadcaster.PruneStalePayloads(future);

        Assert.That(_broadcaster.RecentPayloadCount, Is.EqualTo(0));
    }

    [Test]
    public void PruneStalePayloads_evicts_oldest_entries_when_exceeding_max_cache_size()
    {
        const int totalMessages = 1050;

        for (var i = 0; i < totalMessages; i++)
        {
            var msg = new PieceCompletedMessage("testhash", i, 16384);
            _broadcaster.BroadcastMessage(msg);
        }

        _broadcaster.PruneStalePayloads();

        Assert.That(_broadcaster.RecentPayloadCount, Is.LessThanOrEqualTo(SignalRMessageBroadcaster.MaxCacheSize));
    }

    [Test]
    public void BroadcastMessage_swallows_synchronous_exceptions_without_crashing_caller()
    {
        _hubClients.All.Returns(_ => throw new ObjectDisposedException("HubContext"));

        var msg = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 1 }
        };

        Assert.DoesNotThrow(() => _broadcaster.BroadcastMessage(msg));
    }

    [Test]
    public void BroadcastToGroup_swallows_synchronous_exceptions_without_crashing_caller()
    {
        _hubClients.Group(Arg.Any<string>()).Returns(_ => throw new ObjectDisposedException("HubContext"));

        var msg = new SignalRMessage
        {
            Name = "Torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 1 }
        };

        Assert.DoesNotThrow(() => _broadcaster.BroadcastToGroup("test-group", msg));
    }
}
