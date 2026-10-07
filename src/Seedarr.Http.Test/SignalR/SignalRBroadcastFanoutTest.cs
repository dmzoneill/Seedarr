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
}
