using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.SignalR;

namespace Seedarr.Http.Test.SignalR;

[TestFixture]
public class SignalRBroadcastFanoutTest
{
    [Test]
    public void TryResolveScope_maps_torrent_resource_to_torrents_channel_and_id()
    {
        var resolved = SignalRBroadcastFanout.TryResolveScope("torrent", new { Id = 42 }, out var channel, out var torrentId);

        Assert.That(resolved, Is.True);
        Assert.That(channel, Is.EqualTo("torrents"));
        Assert.That(torrentId, Is.EqualTo(42));
    }

    [Test]
    public void Broadcast_invokes_global_and_scoped_targets()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var message = new SignalRMessage
        {
            Name = "torrent",
            Action = ModelAction.Updated,
            Body = new { Id = 9 }
        };

        SignalRBroadcastFanout.Broadcast(broadcaster, message, "torrents", 9);

        broadcaster.Received(1).BroadcastMessage(message);
        broadcaster.Received(1).BroadcastToChannel("torrents", message);
        broadcaster.Received(1).BroadcastToTorrent(9, message);
    }

    [Test]
    public void Broadcast_should_noop_when_broadcaster_or_message_is_null()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var message = new SignalRMessage { Name = "torrent", Action = ModelAction.Updated };

        SignalRBroadcastFanout.Broadcast(null, message);
        SignalRBroadcastFanout.Broadcast(broadcaster, null);

        broadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Broadcast_should_skip_channel_and_torrent_when_not_provided()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var message = new SignalRMessage { Name = "torrent", Action = ModelAction.Updated };

        SignalRBroadcastFanout.Broadcast(broadcaster, message, channel: "  ", torrentId: 0);

        broadcaster.Received(1).BroadcastMessage(message);
        broadcaster.DidNotReceive().BroadcastToChannel(Arg.Any<string>(), Arg.Any<SignalRMessage>());
        broadcaster.DidNotReceive().BroadcastToTorrent(Arg.Any<int>(), Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Broadcast_should_target_channel_without_torrent_when_id_missing()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var message = new SignalRMessage { Name = "health", Action = ModelAction.Sync };

        SignalRBroadcastFanout.Broadcast(broadcaster, message, channel: "health", torrentId: null);

        broadcaster.Received(1).BroadcastMessage(message);
        broadcaster.Received(1).BroadcastToChannel("health", message);
        broadcaster.DidNotReceive().BroadcastToTorrent(Arg.Any<int>(), Arg.Any<SignalRMessage>());
    }

    [Test]
    public void TryResolveScope_should_map_known_resources()
    {
        Assert.That(SignalRBroadcastFanout.TryResolveScope("trackers", new { TorrentId = 3 }, out var ch, out var id), Is.True);
        Assert.That(ch, Is.EqualTo("trackers"));
        Assert.That(id, Is.EqualTo(3));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("HEALTH", null, out ch, out _), Is.True);
        Assert.That(ch, Is.EqualTo("health"));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("system", null, out ch, out _), Is.True);
        Assert.That(ch, Is.EqualTo("system"));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("task", null, out ch, out _), Is.True);
        Assert.That(ch, Is.EqualTo("tasks"));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("speedschedule", null, out ch, out _), Is.True);
        Assert.That(ch, Is.EqualTo("speeds"));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("automation", null, out ch, out _), Is.True);
        Assert.That(ch, Is.EqualTo("activity"));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("tag", null, out ch, out _), Is.True);
        Assert.That(ch, Is.EqualTo("activity"));
    }

    [Test]
    public void TryResolveScope_should_return_false_for_blank_or_unknown_resources()
    {
        Assert.That(SignalRBroadcastFanout.TryResolveScope("", null, out _, out _), Is.False);
        Assert.That(SignalRBroadcastFanout.TryResolveScope("widgets", null, out _, out _), Is.False);
    }

    [Test]
    public void TryGetPositiveInt_should_support_long_values_and_ignore_invalid()
    {
        Assert.That(SignalRBroadcastFanout.TryGetPositiveInt(new { Id = 5L }, "id"), Is.EqualTo(5));
        Assert.That(SignalRBroadcastFanout.TryGetPositiveInt(new { Id = 0 }, "Id"), Is.Null);
        Assert.That(SignalRBroadcastFanout.TryGetPositiveInt(null, "Id"), Is.Null);
        Assert.That(SignalRBroadcastFanout.TryGetPositiveInt(new { Id = 1 }, ""), Is.Null);
        Assert.That(SignalRBroadcastFanout.TryGetPositiveInt(new { Id = (long)int.MaxValue + 1L }, "Id"), Is.Null);
    }

    [Test]
    public void TryResolveScope_should_cover_remaining_resource_aliases()
    {
        Assert.That(SignalRBroadcastFanout.TryResolveScope("torrents", new { Id = 7 }, out var ch, out var id), Is.True);
        Assert.That(ch, Is.EqualTo("torrents"));
        Assert.That(id, Is.EqualTo(7));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("tracker", new { torrentId = 11 }, out ch, out id), Is.True);
        Assert.That(ch, Is.EqualTo("trackers"));
        Assert.That(id, Is.EqualTo(11));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("command", null, out ch, out _), Is.True);
        Assert.That(ch, Is.EqualTo("system"));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("category", null, out ch, out _), Is.True);
        Assert.That(ch, Is.EqualTo("activity"));

        Assert.That(SignalRBroadcastFanout.TryResolveScope("seeding", null, out ch, out _), Is.True);
        Assert.That(ch, Is.EqualTo("speeds"));
    }

    [Test]
    public void Broadcast_should_skip_torrent_fanout_when_id_is_not_positive()
    {
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var message = new SignalRMessage { Name = "torrent", Action = ModelAction.Updated };

        SignalRBroadcastFanout.Broadcast(broadcaster, message, channel: "torrents", torrentId: -3);

        broadcaster.Received(1).BroadcastMessage(message);
        broadcaster.Received(1).BroadcastToChannel("torrents", message);
        broadcaster.DidNotReceive().BroadcastToTorrent(Arg.Any<int>(), Arg.Any<SignalRMessage>());
    }

    [Test]
    public void TryGetPositiveInt_should_ignore_negative_values()
    {
        Assert.That(SignalRBroadcastFanout.TryGetPositiveInt(new { Id = -1 }, "Id"), Is.Null);
        Assert.That(SignalRBroadcastFanout.TryGetPositiveInt(new { Id = -5L }, "id"), Is.Null);
    }
}
