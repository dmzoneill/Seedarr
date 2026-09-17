using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers.Extensions;

[TestFixture]
public class PexServiceTest
{
    private IConnectionManager _connectionManager;
    private ITorrentService _torrentService;
    private IConfigService _configService;
    private IPeerExchange _peerExchange;
    private PexService _subject;
    private List<PeerConnection> _connections;
    private List<Torrent> _torrents;

    [SetUp]
    public void SetUp()
    {
        _connectionManager = Substitute.For<IConnectionManager>();
        _torrentService = Substitute.For<ITorrentService>();
        _configService = Substitute.For<IConfigService>();
        _connections = new List<PeerConnection>();
        _torrents = new List<Torrent>();

        _configService.EnablePex.Returns(true);
        _configService.ExtensionUtPex.Returns(true);
        _configService.PexInterval.Returns(60);
        _configService.PexMaxPeersPerMessage.Returns(50);

        _peerExchange = new PeerExchange(_configService);

        _torrentService.GetAll().Returns(_torrents);
        _torrentService.GetByInfoHash(Arg.Any<string>()).Returns(x =>
            _torrents.FirstOrDefault(t => string.Equals(t.InfoHash, x.Arg<string>(), StringComparison.OrdinalIgnoreCase)));

        _connectionManager.GetAllConnections().Returns(_connections);
        _connectionManager.GetConnections(Arg.Any<string>()).Returns(x =>
            _connections.Where(c => string.Equals(c.InfoHash, x.Arg<string>(), StringComparison.OrdinalIgnoreCase)).ToList());

        _subject = new PexService(_connectionManager, _peerExchange, _torrentService, _configService);
    }

    [Test]
    public void BroadcastPex_should_send_initial_message_with_active_peers_excluding_recipient()
    {
        var torrent = CreateTorrent("hash123", isPrivate: false);

        var recipient = CreatePeer("127.0.0.1", 5000, torrent.InfoHash, supportsPex: true);
        var peerA = CreatePeer("93.184.216.34", 6881, torrent.InfoHash, supportsPex: true);
        var peerB = CreatePeer("93.184.216.35", 6882, torrent.InfoHash, supportsPex: false);

        _subject.BroadcastPex();

        var messages = GetSentPexMessages(recipient);
        Assert.That(messages, Has.Count.EqualTo(1));

        var pexData = messages[0];
        Assert.That(pexData.Added, Has.Count.EqualTo(2));
        Assert.That(pexData.Added.Any(p => p.Ip == "93.184.216.34" && p.Port == 6881), Is.True);
        Assert.That(pexData.Added.Any(p => p.Ip == "93.184.216.35" && p.Port == 6882), Is.True);

        // Self-exclusion: recipient MUST NOT be in added
        Assert.That(pexData.Added.Any(p => p.Ip == recipient.RemoteIp && p.Port == recipient.RemotePort), Is.False);
        Assert.That(pexData.Dropped, Is.Empty);

        // Tracker state
        Assert.That(recipient.PexTracker.PreviouslySentPeers.Contains("93.184.216.34:6881"), Is.True);
        Assert.That(recipient.PexTracker.PreviouslySentPeers.Contains("93.184.216.35:6882"), Is.True);
        Assert.That(recipient.PexTracker.PreviouslySentPeers.Contains($"{recipient.RemoteIp}:{recipient.RemotePort}"), Is.False);
        Assert.That(recipient.PexTracker.LastPexSent, Is.GreaterThan(DateTime.MinValue));
    }

    [Test]
    public void BroadcastPex_should_send_delta_only_on_second_tick_and_not_resend_active_peers()
    {
        var torrent = CreateTorrent("hash123", isPrivate: false);

        var recipient = CreatePeer("127.0.0.1", 5000, torrent.InfoHash, supportsPex: true);
        var peerA = CreatePeer("93.184.216.34", 6881, torrent.InfoHash, supportsPex: true);
        var peerB = CreatePeer("93.184.216.35", 6882, torrent.InfoHash, supportsPex: true);

        // First tick
        _subject.BroadcastPex();

        var initialMessages = GetSentPexMessages(recipient);
        Assert.That(initialMessages, Has.Count.EqualTo(1));
        ClearPeerStream(recipient);

        // Second tick changes:
        // peerA disconnects
        _connections.Remove(peerA);
        // peerC connects
        var peerC = CreatePeer("93.184.216.36", 6883, torrent.InfoHash, supportsPex: true);
        // peerB remains connected

        _subject.BroadcastPex();

        var secondTickMessages = GetSentPexMessages(recipient);
        Assert.That(secondTickMessages, Has.Count.EqualTo(1));

        var delta = secondTickMessages[0];

        // Newly added peers only
        Assert.That(delta.Added, Has.Count.EqualTo(1));
        Assert.That(delta.Added[0].Ip, Is.EqualTo("93.184.216.36"));
        Assert.That(delta.Added[0].Port, Is.EqualTo(6883));

        // Dropped peers only
        Assert.That(delta.Dropped, Has.Count.EqualTo(1));
        Assert.That(delta.Dropped[0].Ip, Is.EqualTo("93.184.216.34"));
        Assert.That(delta.Dropped[0].Port, Is.EqualTo(6881));

        // Neither peerB nor recipient resent or self-included
        Assert.That(delta.Added.Any(p => p.Ip == "93.184.216.35"), Is.False);
        Assert.That(delta.Added.Any(p => p.Ip == recipient.RemoteIp), Is.False);
        Assert.That(delta.Dropped.Any(p => p.Ip == recipient.RemoteIp), Is.False);

        // Tracker state updated
        Assert.That(recipient.PexTracker.PreviouslySentPeers.Contains("93.184.216.36:6883"), Is.True);
        Assert.That(recipient.PexTracker.PreviouslySentPeers.Contains("93.184.216.35:6882"), Is.True);
        Assert.That(recipient.PexTracker.PreviouslySentPeers.Contains("93.184.216.34:6881"), Is.False);
    }

    [Test]
    public void BroadcastPex_should_never_send_pex_messages_for_private_torrents()
    {
        var torrent = CreateTorrent("private_hash", isPrivate: true);

        var recipient = CreatePeer("127.0.0.1", 5000, torrent.InfoHash, supportsPex: true);
        var peerA = CreatePeer("93.184.216.34", 6881, torrent.InfoHash, supportsPex: true);

        _subject.BroadcastPex();

        var recipientMessages = GetSentPexMessages(recipient);
        Assert.That(recipientMessages, Is.Empty);

        var peerAMessages = GetSentPexMessages(peerA);
        Assert.That(peerAMessages, Is.Empty);

        Assert.That(recipient.PexTracker.PreviouslySentPeers, Is.Empty);
        Assert.That(recipient.PexTracker.LastPexSent, Is.EqualTo(DateTime.MinValue));
    }

    [Test]
    public void BroadcastPex_should_not_send_message_when_no_peers_added_or_dropped()
    {
        var torrent = CreateTorrent("hash123", isPrivate: false);

        var recipient = CreatePeer("127.0.0.1", 5000, torrent.InfoHash, supportsPex: true);
        CreatePeer("93.184.216.34", 6881, torrent.InfoHash, supportsPex: true);

        // First tick
        _subject.BroadcastPex();
        Assert.That(GetSentPexMessages(recipient), Has.Count.EqualTo(1));
        ClearPeerStream(recipient);

        // Second tick with no swarm changes
        _subject.BroadcastPex();
        Assert.That(GetSentPexMessages(recipient), Is.Empty);
    }

    [Test]
    public void BroadcastPex_should_ignore_recipients_that_do_not_support_ut_pex()
    {
        var torrent = CreateTorrent("hash123", isPrivate: false);

        var nonPexRecipient = CreatePeer("127.0.0.1", 5000, torrent.InfoHash, supportsPex: false);
        var pexPeer = CreatePeer("93.184.216.34", 6881, torrent.InfoHash, supportsPex: true);

        _subject.BroadcastPex();

        Assert.That(GetSentPexMessages(nonPexRecipient), Is.Empty);
        Assert.That(GetSentPexMessages(pexPeer), Has.Count.EqualTo(1));
    }

    [Test]
    public void BroadcastPex_should_resend_reconnected_peer_in_added()
    {
        var torrent = CreateTorrent("hash123", isPrivate: false);

        var recipient = CreatePeer("127.0.0.1", 5000, torrent.InfoHash, supportsPex: true);
        var peerA = CreatePeer("93.184.216.34", 6881, torrent.InfoHash, supportsPex: true);

        // Tick 1: A added
        _subject.BroadcastPex();
        ClearPeerStream(recipient);

        // Tick 2: A disconnected
        _connections.Remove(peerA);
        _subject.BroadcastPex();
        ClearPeerStream(recipient);

        // Tick 3: A reconnected
        var peerAReconnected = CreatePeer("93.184.216.34", 6881, torrent.InfoHash, supportsPex: true);
        _subject.BroadcastPex();

        var tick3Messages = GetSentPexMessages(recipient);
        Assert.That(tick3Messages, Has.Count.EqualTo(1));
        Assert.That(tick3Messages[0].Added, Has.Count.EqualTo(1));
        Assert.That(tick3Messages[0].Added[0].Ip, Is.EqualTo("93.184.216.34"));
        Assert.That(tick3Messages[0].Added[0].Port, Is.EqualTo(6881));
        Assert.That(tick3Messages[0].Dropped, Is.Empty);
    }

    private PeerConnection CreatePeer(string ip, int port, string infoHash, bool supportsPex = true)
    {
        var ms = new MemoryStream();
        var conn = new PeerConnection(ms, ip, port)
        {
            InfoHash = infoHash
        };
        if (supportsPex)
        {
            conn.RemoteExtensions["ut_pex"] = 1;
        }

        _connections.Add(conn);
        return conn;
    }

    private Torrent CreateTorrent(string infoHash, bool isPrivate = false)
    {
        var torrent = new Torrent
        {
            InfoHash = infoHash,
            Name = "Torrent-" + infoHash,
            IsPrivate = isPrivate
        };
        _torrents.Add(torrent);
        return torrent;
    }

    private List<PexData> GetSentPexMessages(PeerConnection peer)
    {
        var ms = (MemoryStream)typeof(PeerConnection)
            .GetField("_activeStream", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(peer)!;

        var data = ms.ToArray();
        var messages = new List<PexData>();
        var offset = 0;

        while (offset + 4 <= data.Length)
        {
            var len = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
            if (offset + 4 + len > data.Length)
            {
                break;
            }

            var msgType = data[offset + 4];
            if (msgType == (byte)PeerMessageType.Extended)
            {
                var payload = new byte[len - 2];
                Array.Copy(data, offset + 6, payload, 0, len - 2);
                var parsed = _peerExchange.ParsePexMessage(payload);
                messages.Add(parsed);
            }

            offset += 4 + len;
        }

        return messages;
    }

    private void ClearPeerStream(PeerConnection peer)
    {
        var ms = (MemoryStream)typeof(PeerConnection)
            .GetField("_activeStream", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(peer)!;
        ms.SetLength(0);
    }
}
