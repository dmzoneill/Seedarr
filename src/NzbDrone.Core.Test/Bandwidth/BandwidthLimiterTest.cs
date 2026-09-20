using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Network;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Messages;

namespace NzbDrone.Core.Test.Bandwidth;

[TestFixture]
public class BandwidthLimiterTest
{
    private BandwidthLimiter _limiter;

    [SetUp]
    public void SetUp()
    {
        _limiter = new BandwidthLimiter();
    }

    [TearDown]
    public void TearDown()
    {
        BandwidthLimiter.PaceWaitHandler = BandwidthPacer.PaceWait;
        PeerConnection.PaceWaitHandler = BandwidthPacer.PaceWait;
    }

    [Test]
    public void Hierarchical_limits_should_throttle_bottom_up()
    {
        const string infoHash = "0123456789abcdef0123456789abcdef01234567";
        const string peerId = "-SD0001-123456789012";

        // Global: 100 KB/s, Torrent: 50 KB/s, Peer: 20 KB/s
        _limiter.SetGlobalLimits(100_000, 100_000);
        _limiter.SetTorrentLimits(infoHash, 50_000, 50_000);
        _limiter.SetPeerLimits(peerId, 20_000, 20_000);

        Assert.That(_limiter.HasUploadLimit(infoHash, peerId), Is.True);
        Assert.That(_limiter.HasDownloadLimit(infoHash, peerId), Is.True);

        var peerBucket = _limiter.GetPeerUploadBucket(peerId);
        var torrentBucket = _limiter.GetTorrentUploadBucket(infoHash);
        var globalBucket = _limiter.GlobalUploadBucket;

        // Peer bucket has initial burst allowance
        Assert.That(_limiter.TryConsumeUpload(infoHash, peerId, 5_000), Is.True);

        // Exhaust peer bucket
        peerBucket.TryConsume((long)peerBucket.AvailableTokens);
        Assert.That(peerBucket.AvailableTokens, Is.LessThan(1_000));

        var torrentTokensBefore = torrentBucket.AvailableTokens;
        var globalTokensBefore = globalBucket.AvailableTokens;

        // Since peer bucket cannot satisfy request, consumption fails bottom-up
        // and Torrent/Global tokens are NOT deducted
        Assert.That(_limiter.TryConsumeUpload(infoHash, peerId, 5_000), Is.False);
        Assert.That(torrentBucket.AvailableTokens, Is.GreaterThanOrEqualTo(torrentTokensBefore - 100));
        Assert.That(globalBucket.AvailableTokens, Is.GreaterThanOrEqualTo(globalTokensBefore - 100));
    }

    [Test]
    public void Torrent_limit_should_prevent_swarm_from_starving_other_torrents()
    {
        const string torrentA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string torrentB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        // Global: 100 KB/s, Torrent A: 20 KB/s, Torrent B: 80 KB/s
        _limiter.SetGlobalLimits(100_000, 100_000);
        _limiter.SetTorrentLimits(torrentA, 20_000, 20_000);
        _limiter.SetTorrentLimits(torrentB, 80_000, 80_000);

        var bucketA = _limiter.GetTorrentUploadBucket(torrentA);
        var bucketB = _limiter.GetTorrentUploadBucket(torrentB);

        // Exhaust Torrent A's tokens
        bucketA.TryConsume((long)bucketA.AvailableTokens);

        // Torrent A should now fail to consume
        Assert.That(_limiter.TryConsumeUpload(torrentA, null, 5_000), Is.False);

        // Torrent B can still consume freely from its allocation
        Assert.That(_limiter.TryConsumeUpload(torrentB, null, 10_000), Is.True);
    }

    [Test]
    public void Global_exhaustion_should_refund_child_buckets()
    {
        const string infoHash = "cccccccccccccccccccccccccccccccccccccccc";
        const string peerId = "-SD0001-peer11111111";

        _limiter.SetGlobalLimits(100_000, 100_000);
        _limiter.SetTorrentLimits(infoHash, 50_000, 50_000);
        _limiter.SetPeerLimits(peerId, 25_000, 25_000);

        var peerBucket = _limiter.GetPeerUploadBucket(peerId);
        var torrentBucket = _limiter.GetTorrentUploadBucket(infoHash);
        var globalBucket = _limiter.GlobalUploadBucket;

        // Exhaust global bucket
        globalBucket.TryConsume((long)globalBucket.AvailableTokens);

        var peerTokensBefore = peerBucket.AvailableTokens;
        var torrentTokensBefore = torrentBucket.AvailableTokens;

        // Attempting to consume should fail because Global is exhausted,
        // and both Peer and Torrent buckets should be refunded (not lose 5,000 tokens)
        var result = _limiter.TryConsumeUpload(infoHash, peerId, 5_000);
        Assert.That(result, Is.False);

        // If not refunded, available tokens would be peerTokensBefore - 5000
        Assert.That(peerBucket.AvailableTokens, Is.GreaterThanOrEqualTo(peerTokensBefore - 100));
        Assert.That(torrentBucket.AvailableTokens, Is.GreaterThanOrEqualTo(torrentTokensBefore - 100));
    }

    [Test]
    public void PeerConnection_SendMessage_should_consume_tokens_from_BandwidthLimiter()
    {
        var stream = new MemoryStream();
        var conn = new PeerConnection(stream, "127.0.0.1", 6881)
        {
            InfoHash = "dddddddddddddddddddddddddddddddddddddddd",
            PeerId = "-SD0001-testpeer1234",
            BandwidthLimiter = _limiter,
            PacingChunkSize = 2048
        };

        // Configure upload limit on the limiter
        _limiter.SetTorrentLimits(conn.InfoHash, 50_000, 50_000);

        var torrentBucket = _limiter.GetTorrentUploadBucket(conn.InfoHash);
        var initialTokens = torrentBucket.AvailableTokens;

        var payload = new byte[4096];
        conn.SendMessage(new PeerMessage
        {
            Type = PeerMessageType.Piece,
            Payload = payload,
            PayloadLength = payload.Length
        });

        // 4096 + 5 header = 4101 bytes consumed from torrentBucket
        Assert.That(torrentBucket.AvailableTokens, Is.LessThan(initialTokens - 2000));
        Assert.That(stream.Length, Is.EqualTo(4101));
    }

    [Test]
    public void PeerConnection_ReceiveMessage_should_consume_download_tokens_from_BandwidthLimiter()
    {
        var payload = new byte[4096];
        var length = 1 + payload.Length;
        var rawMessage = new byte[4 + length];
        rawMessage[0] = (byte)(length >> 24);
        rawMessage[1] = (byte)(length >> 16);
        rawMessage[2] = (byte)(length >> 8);
        rawMessage[3] = (byte)length;
        rawMessage[4] = (byte)PeerMessageType.Piece;
        Array.Copy(payload, 0, rawMessage, 5, payload.Length);

        var stream = new MemoryStream(rawMessage);
        var conn = new PeerConnection(stream, "127.0.0.1", 6881)
        {
            InfoHash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            PeerId = "-SD0001-recvpeer1234",
            BandwidthLimiter = _limiter
        };

        _limiter.SetTorrentLimits(conn.InfoHash, 50_000, 50_000);
        var downloadBucket = _limiter.GetTorrentDownloadBucket(conn.InfoHash);
        var initialTokens = downloadBucket.AvailableTokens;

        var msg = conn.ReceiveMessage();
        Assert.That(msg, Is.Not.Null);
        Assert.That(msg.Type, Is.EqualTo(PeerMessageType.Piece));
        Assert.That(downloadBucket.AvailableTokens, Is.LessThan(initialTokens - 2000));
    }

    [Test]
    public void RemoveTorrent_and_RemovePeer_should_cleanup_buckets()
    {
        const string infoHash = "ffffffffffffffffffffffffffffffffffffffff";
        const string peerId = "-SD0001-remove123456";

        _limiter.SetTorrentLimits(infoHash, 10_000, 10_000);
        _limiter.SetPeerLimits(peerId, 5_000, 5_000);

        Assert.That(_limiter.GetTorrentUploadBucket(infoHash), Is.Not.Null);
        Assert.That(_limiter.GetPeerUploadBucket(peerId), Is.Not.Null);

        _limiter.RemoveTorrent(infoHash);
        _limiter.RemovePeer(peerId);

        Assert.That(_limiter.GetTorrentUploadBucket(infoHash), Is.Null);
        Assert.That(_limiter.GetPeerUploadBucket(peerId), Is.Null);
    }
}
