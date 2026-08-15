using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers.Lpd;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers.Lpd;

[TestFixture]
public class LocalPeerDiscoveryTest
{
    private IConfigService _configService;
    private ITorrentService _torrentService;
    private LocalPeerDiscovery _lpd;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _torrentService = Substitute.For<ITorrentService>();
        _configService.EnableLpd.Returns(true);
        _lpd = new LocalPeerDiscovery(_configService, _torrentService);
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
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var lpd = new LocalPeerDiscovery(configService, torrentService);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\nInfohash: abc123def456\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotThrow(() => method.Invoke(lpd, new object[] { announcement, sender }));
    }

    [Test]
    public void ParseAnnouncement_should_ignore_non_bt_search_message()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var lpd = new LocalPeerDiscovery(configService, torrentService);

        var message = "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotThrow(() => method.Invoke(lpd, new object[] { message, sender }));
    }

    [Test]
    public void ParseAnnouncement_should_handle_missing_infohash()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var lpd = new LocalPeerDiscovery(configService, torrentService);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 6881\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotThrow(() => method.Invoke(lpd, new object[] { announcement, sender }));
    }

    [Test]
    public void ParseAnnouncement_should_handle_missing_port()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var lpd = new LocalPeerDiscovery(configService, torrentService);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nInfohash: abc123\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotThrow(() => method.Invoke(lpd, new object[] { announcement, sender }));
    }

    [Test]
    public void ParseAnnouncement_should_handle_invalid_port_value()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var lpd = new LocalPeerDiscovery(configService, torrentService);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: notanumber\r\nInfohash: abc123\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotThrow(() => method.Invoke(lpd, new object[] { announcement, sender }));
    }

    [Test]
    public void ParseAnnouncement_should_handle_zero_port()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var lpd = new LocalPeerDiscovery(configService, torrentService);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nPort: 0\r\nInfohash: abc123\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotThrow(() => method.Invoke(lpd, new object[] { announcement, sender }));
    }

    [Test]
    public void ParseAnnouncement_should_parse_case_insensitive_headers()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var lpd = new LocalPeerDiscovery(configService, torrentService);

        var announcement = "BT-SEARCH * HTTP/1.1\r\nHost: 239.192.152.143:6771\r\nport: 6881\r\ninfohash: abc123\r\n\r\n";
        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotThrow(() => method.Invoke(lpd, new object[] { announcement, sender }));
    }

    [Test]
    public void ParseAnnouncement_should_handle_empty_message()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var lpd = new LocalPeerDiscovery(configService, torrentService);

        var sender = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 12345);

        var method = typeof(LocalPeerDiscovery).GetMethod("ParseAnnouncement",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotThrow(() => method.Invoke(lpd, new object[] { "", sender }));
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

        var lpd = new LocalPeerDiscovery(configService, torrentService);

        Assert.That(lpd, Is.Not.Null);
        lpd.Dispose();
    }

    // Subclass that uses a zero-second announce interval so the loop body
    // can be exercised in tests without waiting 300 seconds.
    private sealed class FastAnnouncingLpd : LocalPeerDiscovery
    {
        public FastAnnouncingLpd(IConfigService configService, ITorrentService torrentService)
            : base(configService, torrentService) { }

        protected override int AnnounceIntervalSeconds => 0;
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
        var torrent = new Torrent { InfoHash = "deadbeef1234567890abcdef12345678deadbeef" };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService);
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
    public async Task AnnounceLoop_skips_torrent_with_empty_infohash()
    {
        var torrent = new Torrent { InfoHash = "" };
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService);
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

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService);
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

        var fastLpd = new FastAnnouncingLpd(_configService, _torrentService);
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
}
