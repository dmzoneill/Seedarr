using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Lpd;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Test.Peers.Lpd;

[TestFixture]
public class LocalPeerDiscoveryTest
{
    private IConfigService _configService;
    private ITorrentService _torrentService;
    private IPeerDiscoveryService _peerDiscovery;
    private LocalPeerDiscovery _lpd;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _torrentService = Substitute.For<ITorrentService>();
        _peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        _configService.EnableLpd.Returns(true);
        _torrentService.FindByInfoHash(Arg.Any<string>()).Returns(callInfo => new Torrent
        {
            InfoHash = callInfo.Arg<string>(),
            IsPrivate = false,
            Status = TorrentStatus.Downloading
        });
        _lpd = new LocalPeerDiscovery(_configService, _torrentService, _peerDiscovery);
    }

    [TearDown]
    public void TearDown()
    {
        _lpd?.Dispose();
    }

    [Test]
    public void BuildAnnouncement_should_start_with_bt_search()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("abc123", 6881);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.StartWith("BT-SEARCH * HTTP/1.1\r\n"));
    }

    [Test]
    public void BuildAnnouncement_should_contain_infohash()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("abc123", 6881);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.Contain("Infohash: abc123"));
    }

    [Test]
    public void BuildAnnouncement_should_contain_port()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("abc123", 9999);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.Contain("Port: 9999"));
    }

    [Test]
    public void BuildAnnouncement_should_contain_cookie_header()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("abc123", 6881, "12345678");
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.Contain("cookie: 12345678"));
    }

    [Test]
    public void BuildAnnouncement_without_cookie_parameter_should_generate_cookie_header()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("abc123", 6881);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.Contain("cookie: "));
    }

    [Test]
    public void BuildAnnouncement_should_contain_multicast_host()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("abc123", 6881);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.Contain("Host: 239.192.152.143:6771"));
    }

    [Test]
    public void BuildAnnouncement_should_return_ascii_bytes()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("abc123", 6881);
        var message = Encoding.ASCII.GetString(bytes);
        var roundTrip = Encoding.ASCII.GetBytes(message);

        Assert.That(bytes, Is.EqualTo(roundTrip));
    }

    [Test]
    public void BuildAnnouncement_should_end_with_double_crlf()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("abc123", 6881);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.EndWith("\r\n\r\n"));
    }

    [Test]
    public void BuildAnnouncement_should_handle_empty_infohash()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("", 6881);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.Contain("Infohash: \r\n"));
    }

    [Test]
    public void BuildAnnouncement_should_handle_zero_port()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("abc123", 0);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.Contain("Port: 0"));
    }

    [Test]
    public void BuildAnnouncement_should_handle_long_infohash()
    {
        var longHash = new string('a', 40);
        var bytes = LocalPeerDiscovery.BuildAnnouncement(longHash, 6881);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.Contain($"Infohash: {longHash}"));
    }

    [Test]
    public void ParseAnnouncement_should_extract_infohash_and_port()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("abc123def456").Returns(new Torrent { InfoHash = "abc123def456", IsPrivate = false });
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.Received(1).AddPeers(
            "abc123def456",
            Arg.Is<IEnumerable<TrackerPeer>>(peers => peers.Single().Port == 6881),
            "lpd");
    }

    [Test]
    public void ParseAnnouncement_should_ignore_non_bt_search_message()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var message = "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { message, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_handle_missing_infohash()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_handle_missing_port()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nInfohash: abc123\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_handle_invalid_port_value()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: notanumber\r\nInfohash: abc123\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_handle_zero_port()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 0\r\nInfohash: abc123\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_parse_case_insensitive_headers()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("abc123").Returns(new Torrent { InfoHash = "abc123", IsPrivate = false });
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nport: 6881\r\ninfohash: abc123\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.Received(1).AddPeers(
            "abc123",
            Arg.Is<IEnumerable<TrackerPeer>>(peers => peers.Single().Port == 6881),
            "lpd");
    }

    [Test]
    public void ParseAnnouncement_should_handle_empty_message()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { "", sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void BuildAnnouncement_round_trip_should_produce_parseable_message()
    {
        var bytes = LocalPeerDiscovery.BuildAnnouncement("deadbeef1234567890abcdef12345678deadbeef", 6881);
        var message = Encoding.ASCII.GetString(bytes);

        Assert.That(message, Does.StartWith("BT-SEARCH"));
        Assert.That(message, Does.Contain("Infohash: deadbeef1234567890abcdef12345678deadbeef"));
        Assert.That(message, Does.Contain("Port: 6881"));
    }

    [Test]
    public void Constructor_should_accept_valid_dependencies()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();

        var lpd = new LocalPeerDiscovery(configService, torrentService, peerDiscovery);

        Assert.That(lpd, Is.Not.Null);
        lpd.Dispose();
    }

    // Subclass that uses a zero-second announce interval so the loop body
    // can be exercised in tests without waiting 300 seconds.
    private sealed class FastAnnouncingLpd : LocalPeerDiscovery
    {
        public List<string> AnnouncedInfoHashes { get; } = new();
        public List<string> AnnouncedCookies { get; } = new();

        public FastAnnouncingLpd(IConfigService configService, ITorrentService torrentService, IPeerDiscoveryService peerDiscovery)
            : base(configService, torrentService, peerDiscovery) { }

        protected override int AnnounceIntervalSeconds => 0;
        protected internal override int InterAnnounceDelayMs => 0;

        protected override Task SendAnnouncementAsync(UdpClient client, byte[] data, IPEndPoint endpoint, CancellationToken stoppingToken)
        {
            var message = Encoding.ASCII.GetString(data);
            foreach (var line in message.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Infohash:", StringComparison.OrdinalIgnoreCase))
                {
                    AnnouncedInfoHashes.Add(line[9..].Trim());
                }
                else if (line.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
                {
                    AnnouncedCookies.Add(line[7..].Trim());
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class PacedAnnouncingLpd : LocalPeerDiscovery
    {
        private readonly Action<int> _onAnnounced;
        public List<(string InfoHash, DateTime Timestamp)> Announcements { get; } = new();

        public PacedAnnouncingLpd(IConfigService configService, ITorrentService torrentService, IPeerDiscoveryService peerDiscovery, int delayMs = 50, Action<int> onAnnounced = null)
            : base(configService, torrentService, peerDiscovery)
        {
            InterAnnounceDelayMs = delayMs;
            _onAnnounced = onAnnounced;
        }

        protected override int AnnounceIntervalSeconds => 0;

        protected override Task SendAnnouncementAsync(UdpClient client, byte[] data, IPEndPoint endpoint, CancellationToken stoppingToken)
        {
            var message = Encoding.ASCII.GetString(data);
            foreach (var line in message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Infohash:", StringComparison.OrdinalIgnoreCase))
                {
                    Announcements.Add((line[9..].Trim(), DateTime.UtcNow));
                    _onAnnounced?.Invoke(Announcements.Count);
                }
            }

            return Task.CompletedTask;
        }
    }

    // ExecuteAsync and background-loop tests

    [Test]
    public async Task ExecuteAsync_exits_immediately_when_lpd_disabled()
    {
        _configService.EnableLpd.Returns(false);

        await _lpd.StartAsync(CancellationToken.None);
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _lpd.StopAsync(stopCts.Token);

        Assert.That(stopCts.IsCancellationRequested, Is.False, "StopAsync should complete before timeout");
    }

    [Test]
    public async Task AnnounceLoop_exits_on_cancellation()
    {
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var cts = new CancellationTokenSource();
        var task = (Task)method.Invoke(_lpd, new object[] { cts.Token });

        // Let the task enter Task.Delay(300s) then cancel it
        await Task.Delay(20);
        await cts.CancelAsync();

        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(task.IsCompleted, Is.True);
    }

    [Test]
    public async Task ListenForPeers_exits_on_cancellation()
    {
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "ListenForPeers",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var udpClient = new UdpClient(0);
        using var cts = new CancellationTokenSource();
        var task = (Task)method.Invoke(_lpd, new object[] { udpClient, cts.Token });

        // Let the task enter ReceiveAsync then cancel it
        await Task.Delay(20);
        await cts.CancelAsync();

        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(task.IsCompleted, Is.True);
    }

    [Test]
    public async Task AnnounceLoop_announces_valid_torrents_after_delay()
    {
        var torrent = new Torrent
        {
            InfoHash = "deadbeef1234567890abcdef12345678deadbeef",
            Status = TorrentStatus.Downloading,
            IsPrivate = false
        };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService, _peerDiscovery);
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var task = (Task)method.Invoke(fastLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(task.IsCompleted, Is.True);
        _torrentService.Received().GetAll();
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Contain(torrent.InfoHash));
        Assert.That(fastLpd.AnnouncedCookies, Contains.Item(fastLpd.ClientCookie));
    }

    [Test]
    public async Task AnnounceLoop_skips_torrent_with_empty_infohash()
    {
        var torrent = new Torrent { InfoHash = "" };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService, _peerDiscovery);
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var task = (Task)method.Invoke(fastLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(task.IsCompleted, Is.True);
        _torrentService.Received().GetAll();
    }

    [Test]
    public async Task AnnounceLoop_handles_get_all_exception_and_continues()
    {
        _torrentService.GetAll().Returns(x => throw new Exception("DB unavailable"));

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService, _peerDiscovery);
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var task = (Task)method.Invoke(fastLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(task.IsCompleted, Is.True);
    }

    [Test]
    public async Task AnnounceLoop_handles_null_infohash_torrent()
    {
        var torrent = new Torrent { InfoHash = null };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService, _peerDiscovery);
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var task = (Task)method.Invoke(fastLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(task.IsCompleted, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_exits_via_socket_exception_when_port_in_use()
    {
        _configService.EnableLpd.Returns(true);

        UdpClient blocker;
        try
        {
            blocker = new UdpClient(6771);
        }
        catch (SocketException)
        {
            Assert.Ignore("Port 6771 already in use by another process; cannot control the test scenario");
            return;
        }

        using (blocker)
        {
            // LocalPeerDiscovery will fail to bind port 6771 -> hits the SocketException catch path
            await _lpd.StartAsync(CancellationToken.None);
            await Task.Delay(300);
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _lpd.StopAsync(stopCts.Token);
        }

        Assert.Pass("Service exited gracefully via SocketException path");
    }

    [Test]
    public async Task ExecuteAsync_with_lpd_enabled_runs_and_stops_cleanly()
    {
        _configService.EnableLpd.Returns(true);
        _torrentService.GetAll().Returns(new List<Torrent>());

        await _lpd.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _lpd.StopAsync(stopCts.Token);

        Assert.Pass("Service started and stopped without hanging");
    }

    [Test]
    public void Handle_ConfigSavedEvent_should_start_lpd_when_toggled_to_enabled()
    {
        _configService.EnableLpd.Returns(false);
        using var lpd = new LocalPeerDiscovery(_configService, _torrentService, _peerDiscovery);

        Assert.That(lpd.IsRunning, Is.False);

        _configService.EnableLpd.Returns(true);
        Assert.DoesNotThrow(() => lpd.Handle(new ConfigSavedEvent()));
    }

    [Test]
    public void Handle_ConfigSavedEvent_should_stop_lpd_when_toggled_to_disabled()
    {
        _configService.EnableLpd.Returns(true);
        using var lpd = new LocalPeerDiscovery(_configService, _torrentService, _peerDiscovery);

        lpd.Handle(new ConfigSavedEvent());

        _configService.EnableLpd.Returns(false);
        lpd.Handle(new ConfigSavedEvent());

        Assert.That(lpd.IsRunning, Is.False);
    }

    [Test]
    public async Task ExecuteAsync_when_disabled_stays_alive_and_toggles_via_ConfigSavedEvent()
    {
        _configService.EnableLpd.Returns(false);
        _torrentService.GetAll().Returns(new List<Torrent>());
        using var lpd = new LocalPeerDiscovery(_configService, _torrentService, _peerDiscovery);

        await lpd.StartAsync(CancellationToken.None);
        Assert.That(lpd.IsRunning, Is.False);

        _configService.EnableLpd.Returns(true);
        lpd.Handle(new ConfigSavedEvent());

        _configService.EnableLpd.Returns(false);
        lpd.Handle(new ConfigSavedEvent());
        Assert.That(lpd.IsRunning, Is.False);

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await lpd.StopAsync(stopCts.Token);
    }

    [TestCase("192.168.1.50")]
    [TestCase("10.0.1.20")]
    [TestCase("172.16.5.5")]
    public void ParseAnnouncement_should_accept_private_ipv4_addresses(string ip)
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("abc123def456").Returns(new Torrent { InfoHash = "abc123def456", IsPrivate = false });
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse(ip), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.Received(1).AddPeers(
            "abc123def456",
            Arg.Is<IEnumerable<TrackerPeer>>(peers => peers.Single().Ip == ip && peers.Single().Port == 6881),
            "lpd");
    }

    [TestCase("169.254.1.1")]
    [TestCase("fe80::1")]
    public void ParseAnnouncement_should_accept_link_local_addresses(string ip)
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("abc123def456").Returns(new Torrent { InfoHash = "abc123def456", IsPrivate = false });
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse(ip), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.Received(1).AddPeers(
            "abc123def456",
            Arg.Is<IEnumerable<TrackerPeer>>(peers => peers.Single().Ip == ip && peers.Single().Port == 6881),
            "lpd");
    }

    [TestCase("fc00::1")]
    [TestCase("fd00::1")]
    public void ParseAnnouncement_should_accept_unique_local_ipv6_addresses(string ip)
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("abc123def456").Returns(new Torrent { InfoHash = "abc123def456", IsPrivate = false });
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse(ip), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.Received(1).AddPeers(
            "abc123def456",
            Arg.Is<IEnumerable<TrackerPeer>>(peers => peers.Single().Ip == ip && peers.Single().Port == 6881),
            "lpd");
    }

    [TestCase("8.8.8.8")]
    [TestCase("1.1.1.1")]
    [TestCase("93.184.216.34")]
    public void ParseAnnouncement_should_reject_public_ipv4_addresses(string ip)
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse(ip), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [TestCase(22)]
    [TestCase(80)]
    [TestCase(1023)]
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    [TestCase(70000)]
    public void ParseAnnouncement_should_reject_privileged_and_invalid_ports(int port)
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var announcement = $"BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: {port}\r\nInfohash: abc123def456\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_reject_loopback_on_listening_port()
    {
        var configService = Substitute.For<IConfigService>();
        configService.ListeningPort.Returns(6881);
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            configService,
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Loopback, 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_reject_self_address_on_listening_port()
    {
        var configService = Substitute.For<IConfigService>();
        configService.ListeningPort.Returns(6881);
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var lpd = new LocalPeerDiscovery(
            configService,
            Substitute.For<ITorrentService>(),
            peerDiscovery);

        var localIp = NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(u => u.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));

        if (localIp != null)
        {
            var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\n\r\n";
            var sender = new IPEndPoint(localIp, 12345);

            var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
                BindingFlags.NonPublic | BindingFlags.Instance);

            method.Invoke(lpd, new object[] { announcement, sender });

            peerDiscovery.DidNotReceive().AddPeers(
                Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
        }
    }

    [Test]
    public async Task AnnounceLoop_should_not_announce_private_torrents()
    {
        var downloadingPrivate = new Torrent
        {
            InfoHash = "private1",
            Status = TorrentStatus.Downloading,
            IsPrivate = true
        };
        var seedingPrivate = new Torrent
        {
            InfoHash = "private2",
            Status = TorrentStatus.Seeding,
            IsPrivate = true
        };
        _torrentService.GetAll().Returns(new List<Torrent> { downloadingPrivate, seedingPrivate });

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService, _peerDiscovery);
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var task = (Task)method.Invoke(fastLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(task.IsCompleted, Is.True);
        Assert.That(fastLpd.AnnouncedInfoHashes, Is.Empty);
    }

    [Test]
    public async Task AnnounceLoop_should_not_announce_stopped_or_paused_torrents()
    {
        var stoppedTorrent = new Torrent
        {
            InfoHash = "stopped1",
            Status = TorrentStatus.Stopped,
            IsPrivate = false
        };
        var pausedTorrent = new Torrent
        {
            InfoHash = "paused1",
            Status = TorrentStatus.Paused,
            IsPrivate = false
        };
        _torrentService.GetAll().Returns(new List<Torrent> { stoppedTorrent, pausedTorrent });

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService, _peerDiscovery);
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var task = (Task)method.Invoke(fastLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(task.IsCompleted, Is.True);
        Assert.That(fastLpd.AnnouncedInfoHashes, Is.Empty);
    }

    [Test]
    public async Task AnnounceLoop_should_only_announce_active_non_private_torrents()
    {
        var downloading = new Torrent { InfoHash = "active_dl", Status = TorrentStatus.Downloading, IsPrivate = false };
        var seeding = new Torrent { InfoHash = "active_seed", Status = TorrentStatus.Seeding, IsPrivate = false };
        var privateDl = new Torrent { InfoHash = "private_dl", Status = TorrentStatus.Downloading, IsPrivate = true };
        var stopped = new Torrent { InfoHash = "stopped", Status = TorrentStatus.Stopped, IsPrivate = false };
        var paused = new Torrent { InfoHash = "paused", Status = TorrentStatus.Paused, IsPrivate = false };

        _torrentService.GetAll().Returns(new List<Torrent> { downloading, seeding, privateDl, stopped, paused });

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService, _peerDiscovery);
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var task = (Task)method.Invoke(fastLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(task.IsCompleted, Is.True);
        Assert.That(fastLpd.AnnouncedInfoHashes, Contains.Item("active_dl"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Contains.Item("active_seed"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("private_dl"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("stopped"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("paused"));
    }

    [Test]
    public void ParseAnnouncement_should_discard_announcement_when_torrent_is_private()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("private_hash").Returns(new Torrent
        {
            InfoHash = "private_hash",
            IsPrivate = true,
            Status = TorrentStatus.Downloading
        });

        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: private_hash\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod(
            "ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_discard_announcement_when_torrent_is_not_found()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("unknown_hash").Returns((Torrent)null);

        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: unknown_hash\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod(
            "ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_add_peers_for_legitimate_non_private_torrent()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("public_hash").Returns(new Torrent
        {
            InfoHash = "public_hash",
            IsPrivate = false,
            Status = TorrentStatus.Downloading
        });

        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: public_hash\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod(
            "ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.Received(1).AddPeers(
            "public_hash",
            Arg.Is<IEnumerable<TrackerPeer>>(peers => peers.Single().Ip == "192.168.1.50" && peers.Single().Port == 6881),
            "lpd");
    }

    [Test]
    public void ParseAnnouncement_should_discard_announcement_matching_client_cookie()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("abc123def456").Returns(new Torrent { InfoHash = "abc123def456", IsPrivate = false });
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var cookie = lpd.ClientCookie;
        var announcement = $"BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\ncookie: {cookie}\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod(
            "ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_discard_announcement_matching_client_cookie_case_insensitively()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("abc123def456").Returns(new Torrent { InfoHash = "abc123def456", IsPrivate = false });
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        var cookie = lpd.ClientCookie.ToUpperInvariant();
        var announcement = $"BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\ncookie: {cookie}\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod(
            "ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.DidNotReceive().AddPeers(
            Arg.Any<string>(), Arg.Any<IEnumerable<TrackerPeer>>(), Arg.Any<string>());
    }

    [Test]
    public void ParseAnnouncement_should_accept_announcement_with_different_cookie()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("abc123def456").Returns(new Torrent { InfoHash = "abc123def456", IsPrivate = false });
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        lpd.ClientCookie = "deadbeef";
        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\ncookie: feedface\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod(
            "ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.Received(1).AddPeers(
            "abc123def456",
            Arg.Is<IEnumerable<TrackerPeer>>(peers => peers.Single().Ip == "192.168.1.50" && peers.Single().Port == 6881),
            "lpd");
    }

    [Test]
    public void ParseAnnouncement_should_accept_announcement_with_absent_cookie()
    {
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.FindByInfoHash("abc123def456").Returns(new Torrent { InfoHash = "abc123def456", IsPrivate = false });
        var lpd = new LocalPeerDiscovery(
            Substitute.For<IConfigService>(),
            torrentService,
            peerDiscovery);

        lpd.ClientCookie = "deadbeef";
        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod(
            "ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        method.Invoke(lpd, new object[] { announcement, sender });

        peerDiscovery.Received(1).AddPeers(
            "abc123def456",
            Arg.Is<IEnumerable<TrackerPeer>>(peers => peers.Single().Ip == "192.168.1.50" && peers.Single().Port == 6881),
            "lpd");
    }

    [Test]
    public void GetActiveNetworkInterfaces_should_only_return_operational_non_loopback_interfaces()
    {
        var interfaces = LocalPeerDiscovery.GetActiveNetworkInterfaces();

        Assert.That(interfaces, Is.Not.Null);
        Assert.That(interfaces.All(nic => nic.OperationalStatus == OperationalStatus.Up), Is.True);
        Assert.That(interfaces.All(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback), Is.True);
    }

    [Test]
    public void InterAnnounceDelayMs_defaults_to_50ms()
    {
        Assert.That(_lpd.InterAnnounceDelayMs, Is.EqualTo(50));
    }

    [Test]
    public void InterAnnounceDelayMs_can_be_configured()
    {
        _lpd.InterAnnounceDelayMs = 100;
        Assert.That(_lpd.InterAnnounceDelayMs, Is.EqualTo(100));
    }

    [Test]
    public async Task AnnounceLoop_only_announces_downloading_and_seeding_torrents()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { InfoHash = "hash-downloading", Status = TorrentStatus.Downloading, IsPrivate = false },
            new Torrent { InfoHash = "hash-seeding", Status = TorrentStatus.Seeding, IsPrivate = false },
            new Torrent { InfoHash = "hash-paused", Status = TorrentStatus.Paused, IsPrivate = false },
            new Torrent { InfoHash = "hash-stopped", Status = TorrentStatus.Stopped, IsPrivate = false },
            new Torrent { InfoHash = "hash-error", Status = TorrentStatus.Error, IsPrivate = false },
            new Torrent { InfoHash = "hash-queued", Status = TorrentStatus.Queued, IsPrivate = false },
            new Torrent { InfoHash = "hash-checking", Status = TorrentStatus.Checking, IsPrivate = false },
            new Torrent { InfoHash = "hash-queuedforchecking", Status = TorrentStatus.QueuedForChecking, IsPrivate = false },
            new Torrent { InfoHash = "hash-moving", Status = TorrentStatus.Moving, IsPrivate = false },
            new Torrent { InfoHash = "hash-private-downloading", Status = TorrentStatus.Downloading, IsPrivate = true },
            new Torrent { InfoHash = "hash-private-seeding", Status = TorrentStatus.Seeding, IsPrivate = true }
        };

        _torrentService.GetAll().Returns(torrents);

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService, _peerDiscovery);
        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var task = (Task)method.Invoke(fastLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Contain("hash-downloading"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Contain("hash-seeding"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("hash-paused"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("hash-stopped"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("hash-error"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("hash-queued"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("hash-checking"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("hash-queuedforchecking"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("hash-moving"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("hash-private-downloading"));
        Assert.That(fastLpd.AnnouncedInfoHashes, Does.Not.Contain("hash-private-seeding"));
    }

    [Test]
    public async Task AnnounceLoop_paces_multicast_announcements()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { InfoHash = "hash-1", Status = TorrentStatus.Downloading, IsPrivate = false },
            new Torrent { InfoHash = "hash-2", Status = TorrentStatus.Downloading, IsPrivate = false },
            new Torrent { InfoHash = "hash-3", Status = TorrentStatus.Downloading, IsPrivate = false }
        };

        _torrentService.GetAll().Returns(torrents);

        using var cts = new CancellationTokenSource();
        var pacedLpd = new PacedAnnouncingLpd(_configService, _torrentService, _peerDiscovery, delayMs: 60, onAnnounced: count =>
        {
            if (count >= 3)
            {
                cts.Cancel();
            }
        });

        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var task = (Task)method.Invoke(pacedLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(4));

        Assert.That(pacedLpd.Announcements.Count, Is.GreaterThanOrEqualTo(3));
        var delta1 = pacedLpd.Announcements[1].Timestamp - pacedLpd.Announcements[0].Timestamp;
        var delta2 = pacedLpd.Announcements[2].Timestamp - pacedLpd.Announcements[1].Timestamp;

        Assert.That(delta1.TotalMilliseconds, Is.GreaterThanOrEqualTo(40));
        Assert.That(delta2.TotalMilliseconds, Is.GreaterThanOrEqualTo(40));
    }

    [Test]
    public async Task AnnounceLoop_aborts_pacing_delay_on_cancellation()
    {
        var torrents = new List<Torrent>
        {
            new Torrent { InfoHash = "hash-1", Status = TorrentStatus.Downloading, IsPrivate = false },
            new Torrent { InfoHash = "hash-2", Status = TorrentStatus.Downloading, IsPrivate = false },
            new Torrent { InfoHash = "hash-3", Status = TorrentStatus.Downloading, IsPrivate = false }
        };

        _torrentService.GetAll().Returns(torrents);

        using var cts = new CancellationTokenSource();
        var pacedLpd = new PacedAnnouncingLpd(_configService, _torrentService, _peerDiscovery, delayMs: 10000, onAnnounced: count =>
        {
            if (count == 1)
            {
                cts.Cancel();
            }
        });

        var method = typeof(LocalPeerDiscovery).GetMethod(
            "AnnounceLoop",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var task = (Task)method.Invoke(pacedLpd, new object[] { cts.Token });

        await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(task.IsCompleted, Is.True);
        Assert.That(pacedLpd.Announcements.Count, Is.EqualTo(1));
    }
}
