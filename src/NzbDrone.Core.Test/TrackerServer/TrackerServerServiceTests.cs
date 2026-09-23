using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;
using NzbDrone.Core.TrackerServer.Users;

namespace NzbDrone.Core.Test.TrackerServerTests;

[TestFixture]
public class TrackerServerServiceTests
{
    private const string DefaultInfoHash = "0123456789abcdef0123456789abcdef01234567";
    private const string DefaultPeerId = "01234567890123456789";

    private Core.TrackerServer.TrackerServer _trackerServer;
    private IPeerDatabase _peerDatabase;
    private IConfigService _configService;
    private ITorrentService _torrentService;
    private ITrackerUserService _trackerUserService;
    private IScrapeCache _scrapeCache;

    [SetUp]
    public void SetUp()
    {
        _peerDatabase = Substitute.For<IPeerDatabase>();
        _configService = Substitute.For<IConfigService>();
        _torrentService = Substitute.For<ITorrentService>();
        _trackerUserService = Substitute.For<ITrackerUserService>();
        _scrapeCache = new ScrapeCache();

        _torrentService.ExistsByInfoHash(DefaultInfoHash).Returns(true);

        _configService.TrackerServerEnabled.Returns(true);
        _configService.TrackerHttpEnabled.Returns(true);
        _configService.TrackerAnnounceInterval.Returns(1800);
        _configService.TrackerMaxPeersPerAnnounce.Returns(50);
        _configService.TrackerLogAnnounces.Returns(false);
        _configService.TrackerPrivateMode.Returns(false);
        _configService.TrackerEnableScrape.Returns(true);
        _configService.TrackerRateLimitPerMinute.Returns(60);
        _configService.MinAnnounceIntervalSeconds.Returns(300);
        _configService.ScrapeIntervalSeconds.Returns(900);
        _configService.TrackerHttpPort.Returns(0);
        _configService.TrackerBindAddress.Returns("127.0.0.1");
        _configService.TrackerPasskeyAuthEnabled.Returns(false);

        _trackerServer = new Core.TrackerServer.TrackerServer(
            _peerDatabase,
            _configService,
            _scrapeCache,
            _trackerUserService,
            _torrentService);
    }

    [TearDown]
    public void TearDown()
    {
        (_scrapeCache as IDisposable)?.Dispose();
    }

    private byte[] InvokeHandleAnnounce(string path, IPEndPoint remoteEndpoint)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "HandleAnnounce",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (byte[])method.Invoke(_trackerServer, new object[] { path, remoteEndpoint });
    }

    private string InvokeHandleAnnounceText(string path, IPEndPoint remoteEndpoint)
    {
        var bytes = InvokeHandleAnnounce(path, remoteEndpoint);
        return Encoding.ASCII.GetString(bytes);
    }

    private byte[] InvokeHandleScrape(string path)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "HandleScrape",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (byte[])method.Invoke(_trackerServer, new object[] { path });
    }

    private string InvokeHandleScrapeText(string path)
    {
        var bytes = InvokeHandleScrape(path);
        return Encoding.Latin1.GetString(bytes);
    }

    private bool InvokeIsRateLimited(string ip, string infoHash = null)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "IsRateLimited",
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[] { typeof(string), typeof(string) });
        return (bool)method.Invoke(_trackerServer, new object[] { ip, infoHash });
    }

    private static byte[] InvokeBuildCompactPeers(List<TrackerPeerEntry> peers, string excludeIp, int excludePort, int maxPeers)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "BuildCompactPeers",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (byte[])method.Invoke(null, new object[] { peers, excludeIp, excludePort, maxPeers });
    }

    private static byte[] InvokeBuildCompactPeers6(List<TrackerPeerEntry> peers, string excludeIp, int excludePort, int maxPeers)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "BuildCompactPeers6",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (byte[])method.Invoke(null, new object[] { peers, excludeIp, excludePort, maxPeers });
    }

    private static BList InvokeBuildDictionaryPeers(List<TrackerPeerEntry> peers, string excludeIp, int excludePort, int maxPeers)
    {
        var method = typeof(Core.TrackerServer.TrackerServer).GetMethod(
            "BuildDictionaryPeers",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (BList)method.Invoke(null, new object[] { peers, excludeIp, excludePort, maxPeers });
    }

    [Test]
    public void Announce_StartEvent_RegistersPeerInDatabase_AndIncrementsAnnounces()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 6881);
        var path = $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=100&downloaded=200&left=500&event=started";

        var responseBytes = InvokeHandleAnnounce(path, endpoint);

        _peerDatabase.Received(1).AddPeer(DefaultInfoHash, "192.168.1.10", 6881, DefaultPeerId, isSeeder: false);
        _peerDatabase.Received(1).IncrementAnnounces();

        var parser = new BencodeParser();
        var responseDict = parser.Parse<BDictionary>(responseBytes);

        responseDict.Should().ContainKey("interval");
        responseDict["interval"].As<BNumber>().Value.Should().Be(1800);
        responseDict.Should().ContainKey("min interval");
        responseDict["min interval"].As<BNumber>().Value.Should().Be(300);
    }

    [Test]
    public void Announce_WhenLeftIsZero_RegistersPeerAsSeeder()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.11"), 6882);
        var path = $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6882&uploaded=5000&downloaded=0&left=0";

        InvokeHandleAnnounce(path, endpoint);

        _peerDatabase.Received(1).AddPeer(DefaultInfoHash, "192.168.1.11", 6882, DefaultPeerId, isSeeder: true);
    }

    [Test]
    public void Announce_StopEvent_RemovesPeerFromDatabase()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.12"), 6883);
        var path = $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6883&uploaded=1000&downloaded=2000&left=0&event=stopped";

        InvokeHandleAnnounce(path, endpoint);

        _peerDatabase.Received(1).RemovePeer(DefaultInfoHash, "192.168.1.12", 6883);
        _peerDatabase.DidNotReceive().AddPeer(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void Announce_CompletedEvent_RecordsCompletionInDatabase()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.13"), 6884);
        var path = $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6884&uploaded=1000&downloaded=5000&left=0&event=completed";

        InvokeHandleAnnounce(path, endpoint);

        _peerDatabase.Received(1).RecordCompleted(DefaultInfoHash);
        _peerDatabase.Received(1).AddPeer(DefaultInfoHash, "192.168.1.13", 6884, DefaultPeerId, isSeeder: true);
    }

    [Test]
    public void Announce_MissingRequiredParameters_ReturnsMissingParametersFailure()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);

        // Missing info_hash
        var res1 = InvokeHandleAnnounceText($"/announce?peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0", endpoint);
        res1.Should().Contain("Missing required parameters");

        // Missing peer_id
        var res2 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&port=6881&uploaded=0&downloaded=0&left=0", endpoint);
        res2.Should().Contain("Missing required parameters");

        // Missing port
        var res3 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&uploaded=0&downloaded=0&left=0", endpoint);
        res3.Should().Contain("Missing required parameters");

        // Missing uploaded
        var res4 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&downloaded=0&left=0", endpoint);
        res4.Should().Contain("Missing required parameters");

        // Missing downloaded
        var res5 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&left=0", endpoint);
        res5.Should().Contain("Missing required parameters");

        // Missing left
        var res6 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0", endpoint);
        res6.Should().Contain("Missing required parameters");
    }

    [Test]
    public void Announce_InvalidParameters_ReturnsInvalidParametersFailure()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);

        // Port out of range (> 65535)
        var res1 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=70000&uploaded=0&downloaded=0&left=0", endpoint);
        res1.Should().Contain("Invalid parameters");

        // Port out of range (<= 0)
        var res2 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=0&uploaded=0&downloaded=0&left=0", endpoint);
        res2.Should().Contain("Invalid parameters");

        // Peer ID length != 20
        var res3 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id=short&port=6881&uploaded=0&downloaded=0&left=0", endpoint);
        res3.Should().Contain("Invalid parameters");

        // Negative numbers
        var res4 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=-1&downloaded=0&left=0", endpoint);
        res4.Should().Contain("Invalid parameters");

        var res5 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=-1&left=0", endpoint);
        res5.Should().Contain("Invalid parameters");

        var res6 = InvokeHandleAnnounceText($"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=-1", endpoint);
        res6.Should().Contain("Invalid parameters");
    }

    [Test]
    public void Announce_MissingQueryString_ReturnsMissingQueryStringError()
    {
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);
        var res = InvokeHandleAnnounceText("/announce", endpoint);
        res.Should().Contain("Missing query string");
    }

    [Test]
    public void Announce_TrafficDelta_RecordedForAuthenticatedUser()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);
        var passkey = "0123456789abcdef0123456789abcdef";
        var user = new TrackerUser
        {
            Passkey = passkey,
            Username = "user1",
            IsEnabled = true,
            IsBanned = false
        };
        _trackerUserService.GetByPasskey(passkey).Returns(user);

        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.15"), 6881);

        // First announce: 1000 uploaded, 500 downloaded
        var path1 = $"/announce?passkey={passkey}&info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=1000&downloaded=500&left=500";
        InvokeHandleAnnounce(path1, endpoint);
        _trackerUserService.Received(1).RecordAnnounce(passkey, 1000L, 500L);

        // Second announce: 2500 uploaded (+1500), 1200 downloaded (+700)
        var path2 = $"/announce?passkey={passkey}&info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=2500&downloaded=1200&left=0";
        InvokeHandleAnnounce(path2, endpoint);
        _trackerUserService.Received(1).RecordAnnounce(passkey, 1500L, 700L);
    }

    [Test]
    public void Scrape_SingleInfoHash_ReturnsStatsForRequestedHash()
    {
        _peerDatabase.GetStats(DefaultInfoHash).Returns(new ScrapeStats
        {
            Complete = 5,
            Incomplete = 2,
            Downloaded = 10
        });

        var responseBytes = InvokeHandleScrape($"/scrape?info_hash={DefaultInfoHash}");
        _peerDatabase.Received(1).IncrementScrapes();

        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(responseBytes);

        dict.Should().ContainKey("files");
        dict.Should().ContainKey("min_request_interval");
        dict["min_request_interval"].As<BNumber>().Value.Should().Be(900);

        var files = dict["files"].As<BDictionary>();
        var hashBytes = Convert.FromHexString(DefaultInfoHash);
        var fileStats = files[new BString(hashBytes)].As<BDictionary>();

        fileStats["complete"].As<BNumber>().Value.Should().Be(5);
        fileStats["incomplete"].As<BNumber>().Value.Should().Be(2);
        fileStats["downloaded"].As<BNumber>().Value.Should().Be(10);
    }

    [Test]
    public void Scrape_MultipleInfoHashes_ReturnsStatsForEachHash()
    {
        var hash2 = "fedcba9876543210fedcba9876543210fedcba98";
        _peerDatabase.GetStats(DefaultInfoHash).Returns(new ScrapeStats { Complete = 3, Incomplete = 1, Downloaded = 7 });
        _peerDatabase.GetStats(hash2).Returns(new ScrapeStats { Complete = 8, Incomplete = 4, Downloaded = 20 });

        var responseBytes = InvokeHandleScrape($"/scrape?info_hash={DefaultInfoHash}&info_hash={hash2}");

        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(responseBytes);
        var files = dict["files"].As<BDictionary>();

        var fileStats1 = files[new BString(Convert.FromHexString(DefaultInfoHash))].As<BDictionary>();
        fileStats1["complete"].As<BNumber>().Value.Should().Be(3);

        var fileStats2 = files[new BString(Convert.FromHexString(hash2))].As<BDictionary>();
        fileStats2["complete"].As<BNumber>().Value.Should().Be(8);
    }

    [Test]
    public void Scrape_FullScrape_ReturnsAllSwarmStats()
    {
        var allStats = new Dictionary<string, ScrapeStats>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultInfoHash] = new ScrapeStats { Complete = 12, Incomplete = 4, Downloaded = 30 }
        };
        _peerDatabase.GetAllStats().Returns(allStats);

        var responseBytes = InvokeHandleScrape("/scrape");

        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(responseBytes);
        var files = dict["files"].As<BDictionary>();

        var stats = files[new BString(Convert.FromHexString(DefaultInfoHash))].As<BDictionary>();
        stats["complete"].As<BNumber>().Value.Should().Be(12);
        stats["incomplete"].As<BNumber>().Value.Should().Be(4);
        stats["downloaded"].As<BNumber>().Value.Should().Be(30);
    }

    [Test]
    public void Scrape_WhenScrapeDisabled_ReturnsScrapeDisabledFailureReason()
    {
        _configService.TrackerEnableScrape.Returns(false);

        var response = InvokeHandleScrapeText($"/scrape?info_hash={DefaultInfoHash}");
        response.Should().Contain("Scrape disabled");
    }

    [Test]
    public void PeerDatabase_AddPeer_StoresLeecherAndSeederProperly()
    {
        using var db = new PeerDatabase(TimeSpan.Zero);

        db.AddPeer("hash1", "10.0.0.1", 6881, "peer1_______________", isSeeder: false);
        db.AddPeer("hash1", "10.0.0.2", 6882, "peer2_______________", isSeeder: true);

        var peers = db.GetPeers("hash1");
        peers.Should().HaveCount(2);

        var stats = db.GetStats("hash1");
        stats.Complete.Should().Be(1);
        stats.Incomplete.Should().Be(1);
        stats.Downloaded.Should().Be(0);
    }

    [Test]
    public void PeerDatabase_RemovePeer_RemovesSpecifiedPeer()
    {
        using var db = new PeerDatabase(TimeSpan.Zero);

        db.AddPeer("hash1", "10.0.0.1", 6881, "peer1_______________", isSeeder: false);
        db.AddPeer("hash1", "10.0.0.2", 6882, "peer2_______________", isSeeder: true);

        db.RemovePeer("hash1", "10.0.0.1", 6881);

        var peers = db.GetPeers("hash1");
        peers.Should().HaveCount(1);
        peers[0].Ip.Should().Be("10.0.0.2");
        peers[0].Port.Should().Be(6882);
    }

    [Test]
    public void PeerDatabase_RecordCompleted_IncrementsDownloaded()
    {
        using var db = new PeerDatabase(TimeSpan.Zero);

        db.RecordCompleted("hash1");
        db.RecordCompleted("hash1");

        var stats = db.GetStats("hash1");
        stats.Downloaded.Should().Be(2);
    }

    [Test]
    public void PeerDatabase_PruneStalePeers_RemovesPeersExceedingTtl()
    {
        using var db = new PeerDatabase(TimeSpan.Zero);

        db.AddPeer("hash1", "10.0.0.1", 6881, "peer1_______________", isSeeder: true);
        var peers = db.GetPeers("hash1");
        peers[0].LastAnnounce = DateTime.UtcNow.AddMinutes(-50); // Older than 45 min TTL

        var evicted = db.PruneStalePeers();
        evicted.Should().Be(1);

        db.GetPeers("hash1").Should().BeEmpty();
        db.ContainsSwarm("hash1").Should().BeFalse();
    }

    [Test]
    public void PeerDatabase_GetAllStats_AndTotals_ReturnAccurateMetrics()
    {
        using var db = new PeerDatabase(TimeSpan.Zero);

        db.AddPeer("hash1", "10.0.0.1", 6881, "peer1_______________", isSeeder: true);
        db.AddPeer("hash2", "10.0.0.2", 6882, "peer2_______________", isSeeder: false);

        db.GetTotalPeerCount().Should().Be(2);
        db.GetTotalTorrentCount().Should().Be(2);

        var allHashes = db.GetAllInfoHashes();
        allHashes.Should().Contain(new[] { "hash1", "hash2" });

        var allStats = db.GetAllStats();
        allStats.Should().ContainKey("hash1");
        allStats.Should().ContainKey("hash2");
        allStats["hash1"].Complete.Should().Be(1);
        allStats["hash2"].Incomplete.Should().Be(1);
    }

    [Test]
    public void BuildCompactPeers_IPv4_FormatsSixBytesPerPeer_InNetworkOrder()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new() { Ip = "192.168.1.1", Port = 6881 },
            new() { Ip = "10.0.0.1", Port = 51413 }
        };

        var bytes = InvokeBuildCompactPeers(peers, excludeIp: "127.0.0.1", excludePort: 0, maxPeers: 50);

        bytes.Length.Should().Be(12);

        // First peer: 192.168.1.1:6881 (0x1AD1)
        bytes[0].Should().Be(192);
        bytes[1].Should().Be(168);
        bytes[2].Should().Be(1);
        bytes[3].Should().Be(1);
        bytes[4].Should().Be(0x1A);
        bytes[5].Should().Be(0xD1);

        // Second peer: 10.0.0.1:51413 (0xC8D5)
        bytes[6].Should().Be(10);
        bytes[7].Should().Be(0);
        bytes[8].Should().Be(0);
        bytes[9].Should().Be(1);
        bytes[10].Should().Be(0xC8);
        bytes[11].Should().Be(0xD5);
    }

    [Test]
    public void BuildCompactPeers6_IPv6_FormatsEighteenBytesPerPeer()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new() { Ip = "2001:db8::1", Port = 6881 }
        };

        var bytes = InvokeBuildCompactPeers6(peers, excludeIp: "127.0.0.1", excludePort: 0, maxPeers: 50);

        bytes.Length.Should().Be(18);
        bytes[16].Should().Be(0x1A);
        bytes[17].Should().Be(0xD1);
    }

    [Test]
    public void BuildCompactPeers_ExcludesRequestingPeerEndpoint()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new() { Ip = "192.168.1.1", Port = 6881 },
            new() { Ip = "10.0.0.1", Port = 6881 }
        };

        var bytes = InvokeBuildCompactPeers(peers, excludeIp: "192.168.1.1", excludePort: 6881, maxPeers: 50);

        bytes.Length.Should().Be(6);
        bytes[0].Should().Be(10);
        bytes[1].Should().Be(0);
        bytes[2].Should().Be(0);
        bytes[3].Should().Be(1);
    }

    [Test]
    public void BuildDictionaryPeers_CompactZero_ReturnsBListOfPeerDictionaries()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new() { Ip = "10.0.0.1", Port = 6881, PeerId = "peer1_______________" }
        };

        var list = InvokeBuildDictionaryPeers(peers, excludeIp: "192.168.1.1", excludePort: 0, maxPeers: 50);

        list.Count.Should().Be(1);
        var dict = list[0].As<BDictionary>();
        dict["ip"].As<BString>().ToString().Should().Be("10.0.0.1");
        dict["port"].As<BNumber>().Value.Should().Be(6881);
        dict["peer id"].As<BString>().ToString().Should().Be("peer1_______________");
    }

    [Test]
    public void Announce_WhenCompactZero_ReturnsPeersAsBList()
    {
        _peerDatabase.GetPeers(DefaultInfoHash).Returns(new List<TrackerPeerEntry>
        {
            new() { Ip = "10.0.0.2", Port = 6882, PeerId = "peer2_______________" }
        });

        var endpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 6881);
        var path = $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0&compact=0";

        var responseBytes = InvokeHandleAnnounce(path, endpoint);

        var parser = new BencodeParser();
        var responseDict = parser.Parse<BDictionary>(responseBytes);

        responseDict.Should().ContainKey("peers");
        responseDict["peers"].Should().BeOfType<BList>();

        var peerList = responseDict["peers"].As<BList>();
        peerList.Count.Should().Be(1);
        peerList[0].As<BDictionary>()["ip"].As<BString>().ToString().Should().Be("10.0.0.2");
    }

    [Test]
    public void BuildPeers_RespectsMaxPeersPerAnnounce()
    {
        var peers = new List<TrackerPeerEntry>();
        for (var i = 1; i <= 20; i++)
        {
            peers.Add(new TrackerPeerEntry { Ip = $"10.0.0.{i}", Port = 6881 });
        }

        var compact = InvokeBuildCompactPeers(peers, excludeIp: "127.0.0.1", excludePort: 0, maxPeers: 5);
        compact.Length.Should().Be(5 * 6);

        var dictList = InvokeBuildDictionaryPeers(peers, excludeIp: "127.0.0.1", excludePort: 0, maxPeers: 5);
        dictList.Count.Should().Be(5);
    }

    [Test]
    public void Announce_ResponseIntervals_MatchConfiguration()
    {
        _configService.TrackerAnnounceInterval.Returns(3600);
        _configService.MinAnnounceIntervalSeconds.Returns(600);

        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);
        var path = $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0";

        var bytes = InvokeHandleAnnounce(path, endpoint);
        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(bytes);

        dict["interval"].As<BNumber>().Value.Should().Be(3600);
        dict["min interval"].As<BNumber>().Value.Should().Be(600);
    }

    [Test]
    public void Scrape_ResponseInterval_MatchesConfiguration()
    {
        _configService.ScrapeIntervalSeconds.Returns(1200);

        var bytes = InvokeHandleScrape($"/scrape?info_hash={DefaultInfoHash}");
        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(bytes);

        dict["min_request_interval"].As<BNumber>().Value.Should().Be(1200);
    }

    [Test]
    public void PasskeyAuth_WhenEnabledAndPasskeyMissing_ReturnsMissingPasskeyError()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);
        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);
        var path = $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0";

        var res = InvokeHandleAnnounceText(path, endpoint);
        res.Should().Contain("Missing announce passkey");
    }

    [Test]
    public void PasskeyAuth_WhenPasskeyNotFound_ReturnsInvalidOrRevokedPasskey()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);
        _trackerUserService.GetByPasskey("unknown_passkey").Returns((TrackerUser)null);

        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);
        var path = $"/announce?passkey=unknown_passkey&info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0";

        var res = InvokeHandleAnnounceText(path, endpoint);
        res.Should().Contain("Invalid or revoked passkey");
    }

    [Test]
    public void PasskeyAuth_WhenUserDisabled_ReturnsInvalidOrRevokedPasskey()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);
        var passkey = "disabled_user_key_1234567890123";
        _trackerUserService.GetByPasskey(passkey).Returns(new TrackerUser
        {
            Passkey = passkey,
            IsEnabled = false,
            IsBanned = false
        });

        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);
        var path = $"/announce/{passkey}?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0";

        var res = InvokeHandleAnnounceText(path, endpoint);
        res.Should().Contain("Invalid or revoked passkey");
    }

    [Test]
    public void PasskeyAuth_WhenUserBanned_BlacklistsRequest()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);
        var passkey = "banned_user_key_123456789012345";
        _trackerUserService.GetByPasskey(passkey).Returns(new TrackerUser
        {
            Passkey = passkey,
            IsEnabled = true,
            IsBanned = true
        });

        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);
        var path = $"/announce/{passkey}?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0";

        var res = InvokeHandleAnnounceText(path, endpoint);
        res.Should().Contain("Invalid or revoked passkey");
    }

    [Test]
    public void PasskeyAuth_WhenUserValidAndEnabled_AllowsAnnounce()
    {
        _configService.TrackerPasskeyAuthEnabled.Returns(true);
        var passkey = "valid_user_key_1234567890123456";
        _trackerUserService.GetByPasskey(passkey).Returns(new TrackerUser
        {
            Passkey = passkey,
            IsEnabled = true,
            IsBanned = false
        });

        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);
        var path = $"/announce/{passkey}?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0";

        var bytes = InvokeHandleAnnounce(path, endpoint);
        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(bytes);

        dict.Should().ContainKey("interval");
    }

    [Test]
    public void PrivateMode_WhenEnabledAndTorrentUnregistered_ReturnsFailureReason()
    {
        _configService.TrackerPrivateMode.Returns(true);
        var unregisteredHash = "9999999999999999999999999999999999999999";
        _torrentService.ExistsByInfoHash(unregisteredHash).Returns(false);

        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);
        var path = $"/announce?info_hash={unregisteredHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0";

        var res = InvokeHandleAnnounceText(path, endpoint);
        res.Should().Contain("torrent not registered with tracker");
    }

    [Test]
    public void PrivateMode_WhenEnabledAndTorrentRegistered_IncludesPrivateFlag()
    {
        _configService.TrackerPrivateMode.Returns(true);
        _torrentService.ExistsByInfoHash(DefaultInfoHash).Returns(true);

        var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6881);
        var path = $"/announce?info_hash={DefaultInfoHash}&peer_id={DefaultPeerId}&port=6881&uploaded=0&downloaded=0&left=0";

        var bytes = InvokeHandleAnnounce(path, endpoint);
        var parser = new BencodeParser();
        var dict = parser.Parse<BDictionary>(bytes);

        dict.Should().ContainKey("private");
        dict["private"].As<BNumber>().Value.Should().Be(1);
    }

    [Test]
    public void RateLimiting_DetectsExcessiveRequests()
    {
        _configService.TrackerRateLimitPerMinute.Returns(3);

        var ip = "10.10.10.10";

        InvokeIsRateLimited(ip, DefaultInfoHash).Should().BeFalse();
        InvokeIsRateLimited(ip, DefaultInfoHash).Should().BeFalse();
        InvokeIsRateLimited(ip, DefaultInfoHash).Should().BeFalse();

        // 4th request exceeds limit of 3
        InvokeIsRateLimited(ip, DefaultInfoHash).Should().BeTrue();
    }

    [Test]
    public void RateLimiting_DisabledWhenConfiguredZeroOrNegative()
    {
        _configService.TrackerRateLimitPerMinute.Returns(0);

        var ip = "10.10.10.11";

        for (var i = 0; i < 100; i++)
        {
            InvokeIsRateLimited(ip, DefaultInfoHash).Should().BeFalse();
        }
    }

}
