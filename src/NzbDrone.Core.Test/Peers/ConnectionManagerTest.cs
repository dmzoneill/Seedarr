using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class ConnectionManagerTest
{
    private IConfigService _configService;
    private IPeerConnectionLogService _connectionLogService;
    private ITorrentService _torrentService;
    private IFastExtensionHandler _fastExtensionHandler;
    private ITorrentEventLogService _eventLogService;
    private ConnectionManager _manager;
    private List<PeerConnection> _createdConnections;
    private List<TcpListener> _listeners;
    private List<TcpClient> _serverClients;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _connectionLogService = Substitute.For<IPeerConnectionLogService>();
        _torrentService = Substitute.For<ITorrentService>();
        _fastExtensionHandler = Substitute.For<IFastExtensionHandler>();
        _eventLogService = Substitute.For<ITorrentEventLogService>();
        _manager = new ConnectionManager(_configService, _connectionLogService, _torrentService, _fastExtensionHandler, _eventLogService);

        _createdConnections = new List<PeerConnection>();
        _listeners = new List<TcpListener>();
        _serverClients = new List<TcpClient>();

        _configService.MaxGlobalConnections.Returns(200);
        _configService.MaxPerTorrentConnections.Returns(50);
        _configService.MaxUploadSlots.Returns(4);
        _configService.PeerDropoutProbability.Returns(0.0);
        _configService.ConnectionRotationPercentage.Returns(0.0);
        _torrentService.GetAll().Returns(new List<Torrent>());
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var conn in _createdConnections)
        {
            try
            {
                conn.Dispose();
            }
            catch
            {
            }
        }

        foreach (var client in _serverClients)
        {
            try
            {
                client.Dispose();
            }
            catch
            {
            }
        }

        foreach (var listener in _listeners)
        {
            try
            {
                listener.Stop();
            }
            catch
            {
            }
        }
    }

    private PeerConnection CreateTestConnection(DateTime? connectedAt = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new TcpClient();
        client.Connect(IPAddress.Loopback, port);
        var serverClient = listener.AcceptTcpClient();
        _serverClients.Add(client);
        listener.Stop();

        var conn = new PeerConnection(serverClient)
        {
            ConnectedAt = connectedAt ?? DateTime.UtcNow.AddMinutes(-2)
        };
        _createdConnections.Add(conn);
        return conn;
    }

    private void SetInfoHash(PeerConnection conn, string infoHash)
    {
        typeof(PeerConnection).GetProperty("InfoHash").SetValue(conn, infoHash);
    }

    [Test]
    public void ActiveCount_should_return_zero_initially()
    {
        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void Add_should_increase_active_count()
    {
        var conn = CreateTestConnection();

        _manager.Add(conn);

        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public void Add_should_log_connect_event()
    {
        var conn = CreateTestConnection();

        _manager.Add(conn);

        _connectionLogService.Received(1).LogConnected(conn, Arg.Any<string>());
    }

    [Test]
    public void Add_should_evict_oldest_connection_when_at_max_global()
    {
        _configService.MaxGlobalConnections.Returns(1);
        var first = CreateTestConnection();
        var second = CreateTestConnection();

        _manager.Add(first);
        _manager.Add(second);

        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
        _fastExtensionHandler.Received(1).UnregisterPeer(first);
    }

    [Test]
    public void Add_should_log_disconnect_for_evicted_connection()
    {
        _configService.MaxGlobalConnections.Returns(1);
        var first = CreateTestConnection();
        var second = CreateTestConnection();

        _manager.Add(first);
        _manager.Add(second);

        _connectionLogService.Received(1).LogDisconnected(first, Arg.Any<string>());
    }

    [Test]
    public void Remove_should_decrease_active_count()
    {
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.Remove(conn);

        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void Remove_should_unregister_fast_peer()
    {
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.Remove(conn);

        _fastExtensionHandler.Received(1).UnregisterPeer(conn);
    }

    [Test]
    public void Remove_should_dispose_connection()
    {
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.Remove(conn);

        Assert.That(conn.IsConnected, Is.False);
    }

    [Test]
    public void Remove_should_log_disconnect_event()
    {
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.Remove(conn);

        _connectionLogService.Received(1).LogDisconnected(conn, Arg.Any<string>());
    }

    [Test]
    public void GetConnections_should_return_matching_info_hash()
    {
        var conn = CreateTestConnection();
        SetInfoHash(conn, "abc123");
        _manager.Add(conn);

        var result = _manager.GetConnections("abc123");

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0], Is.SameAs(conn));
    }

    [Test]
    public void GetConnections_should_return_empty_for_unknown_hash()
    {
        var conn = CreateTestConnection();
        SetInfoHash(conn, "abc123");
        _manager.Add(conn);

        var result = _manager.GetConnections("xyz789");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetConnections_should_be_case_insensitive()
    {
        var conn = CreateTestConnection();
        SetInfoHash(conn, "AbCdEf");
        _manager.Add(conn);

        var result = _manager.GetConnections("abcdef");

        Assert.That(result.Count, Is.EqualTo(1));
    }

    [Test]
    public void CanAddConnectionForTorrent_should_return_true_when_under_limit()
    {
        _configService.MaxPerTorrentConnections.Returns(5);
        var conn = CreateTestConnection();
        SetInfoHash(conn, "abc123");
        _manager.Add(conn);

        var result = _manager.CanAddConnectionForTorrent("abc123");

        Assert.That(result, Is.True);
    }

    [Test]
    public void CanAddConnectionForTorrent_should_return_false_when_at_limit()
    {
        _configService.MaxPerTorrentConnections.Returns(1);
        var conn = CreateTestConnection();
        SetInfoHash(conn, "abc123");
        _manager.Add(conn);

        var result = _manager.CanAddConnectionForTorrent("abc123");

        Assert.That(result, Is.False);
    }

    [Test]
    public void CanAddConnectionForTorrent_should_return_true_for_different_hash()
    {
        _configService.MaxPerTorrentConnections.Returns(1);
        var conn = CreateTestConnection();
        SetInfoHash(conn, "abc123");
        _manager.Add(conn);

        var result = _manager.CanAddConnectionForTorrent("xyz789");

        Assert.That(result, Is.True);
    }

    [Test]
    public void CanAddConnectionForTorrent_should_return_false_when_at_global_limit()
    {
        _configService.MaxGlobalConnections.Returns(1);
        _configService.MaxPerTorrentConnections.Returns(50);
        var conn = CreateTestConnection();
        SetInfoHash(conn, "abc123");
        _manager.Add(conn);

        var result = _manager.CanAddConnectionForTorrent("xyz789");

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetUploadSlotCount_should_return_config_value()
    {
        _configService.MaxUploadSlots.Returns(8);

        var result = _manager.GetUploadSlotCount();

        Assert.That(result, Is.EqualTo(8));
    }

    [Test]
    public void GetDynamicUploadSlotCount_with_low_bandwidth_cap_yields_at_least_four_slots()
    {
        _configService.MaxUploadSpeedKbps.Returns(50);
        _configService.MaxUploadSlots.Returns(0);

        var slots = _manager.GetDynamicUploadSlotCount();

        Assert.That(slots, Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public void GetDynamicUploadSlotCount_with_high_bandwidth_cap_scales_up_slots_according_to_sqrt_formula()
    {
        _configService.MaxUploadSpeedKbps.Returns(10000);
        _configService.MaxUploadSlots.Returns(0);

        var expected = (int)Math.Ceiling(Math.Sqrt(2.0 * 10000));
        var slots = _manager.GetDynamicUploadSlotCount();

        Assert.That(slots, Is.EqualTo(expected));
    }

    [Test]
    public void GetDynamicUploadSlotCount_with_unlimited_bandwidth_scales_with_active_leechers()
    {
        _configService.MaxUploadSpeedKbps.Returns(0);
        _configService.MaxUploadSlots.Returns(20);

        for (var i = 1; i <= 50; i++)
        {
            var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 1000 + i);
            SetInfoHash(conn, "hashX");
            conn.IsSeed = false;
            _manager.Add(conn);
        }

        var slots = _manager.GetDynamicUploadSlotCount("hashX");

        // 50 active leechers * 0.2 = 10 slots (clamped between 4 and 20)
        Assert.That(slots, Is.EqualTo(10));
    }

    [Test]
    public void GetDynamicUploadSlotCount_with_unlimited_bandwidth_and_no_active_leechers_falls_back_to_max_upload_slots()
    {
        _configService.MaxUploadSpeedKbps.Returns(0);
        _configService.MaxUploadSlots.Returns(8);

        Assert.That(_manager.GetDynamicUploadSlotCount(), Is.EqualTo(8));
        Assert.That(_manager.GetDynamicUploadSlotCount("emptyHash"), Is.EqualTo(8));
    }

    [Test]
    public void ProcessDropouts_should_return_early_when_probability_is_zero()
    {
        _configService.PeerDropoutProbability.Returns(0.0);
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.ProcessDropouts();

        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public void ProcessDropouts_should_return_early_when_no_connections()
    {
        _configService.PeerDropoutProbability.Returns(0.5);

        _manager.ProcessDropouts();

        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void RotateConnections_should_return_early_when_no_connections()
    {
        _configService.ConnectionRotationPercentage.Returns(0.5);

        _manager.RotateConnections();

        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void RotateConnections_should_remove_oldest_connections()
    {
        _configService.ConnectionRotationPercentage.Returns(1.0);
        var conn1 = CreateTestConnection();
        var conn2 = CreateTestConnection();
        _manager.Add(conn1);
        _manager.Add(conn2);

        _manager.RotateConnections();

        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
        _fastExtensionHandler.Received(1).UnregisterPeer(conn1);
        _fastExtensionHandler.Received(1).UnregisterPeer(conn2);
    }

    [Test]
    public void RotateConnections_should_not_remove_when_percentage_is_zero()
    {
        _configService.ConnectionRotationPercentage.Returns(0.0);
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.RotateConnections();

        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public void Add_should_keep_count_at_max_after_eviction()
    {
        _configService.MaxGlobalConnections.Returns(2);
        var first = CreateTestConnection();
        var second = CreateTestConnection();
        var third = CreateTestConnection();

        _manager.Add(first);
        _manager.Add(second);
        _manager.Add(third);

        Assert.That(_manager.ActiveCount, Is.EqualTo(2));
    }

    [Test]
    public void ProcessDropouts_should_remove_all_connections_when_probability_is_one()
    {
        _configService.PeerDropoutProbability.Returns(1.0);
        var conn1 = CreateTestConnection();
        var conn2 = CreateTestConnection();
        _manager.Add(conn1);
        _manager.Add(conn2);

        _manager.ProcessDropouts();

        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
        _fastExtensionHandler.Received(1).UnregisterPeer(conn1);
        _fastExtensionHandler.Received(1).UnregisterPeer(conn2);
    }

    [Test]
    public void ProcessDropouts_should_log_disconnects_for_removed_connections()
    {
        _configService.PeerDropoutProbability.Returns(1.0);
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.ProcessDropouts();

        _connectionLogService.Received(1).LogDisconnected(conn, Arg.Any<string>());
    }

    [Test]
    public void RotateConnections_should_remove_partial_connections_by_percentage()
    {
        _configService.ConnectionRotationPercentage.Returns(0.5);
        var conn1 = CreateTestConnection();
        var conn2 = CreateTestConnection();
        _manager.Add(conn1);
        _manager.Add(conn2);

        _manager.RotateConnections();

        // ceil(2 * 0.5) = 1 connection removed
        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public void RotateConnections_should_log_disconnect_for_rotated_connection()
    {
        _configService.ConnectionRotationPercentage.Returns(1.0);
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.RotateConnections();

        _connectionLogService.Received(1).LogDisconnected(conn, Arg.Any<string>());
    }

    [Test]
    public void RotateConnections_should_protect_top_performing_peers_with_high_download_upload_speeds_from_eviction()
    {
        _configService.ConnectionRotationPercentage.Returns(0.5);

        var fastPeer1 = CreateTestConnection();
        fastPeer1.DownloadSpeed = 1_000_000;

        var fastPeer2 = CreateTestConnection();
        fastPeer2.UploadSpeed = 500_000;

        var slowPeer1 = CreateTestConnection();
        slowPeer1.DownloadSpeed = 0;
        slowPeer1.UploadSpeed = 0;

        var slowPeer2 = CreateTestConnection();
        slowPeer2.DownloadSpeed = 0;
        slowPeer2.UploadSpeed = 0;

        _manager.Add(fastPeer1);
        _manager.Add(fastPeer2);
        _manager.Add(slowPeer1);
        _manager.Add(slowPeer2);

        _manager.RotateConnections();

        // 4 * 0.5 = 2 connections removed (slow peers). Top 2 fast peers must be preserved.
        Assert.That(_manager.ActiveCount, Is.EqualTo(2));
        var remaining = _manager.GetAllConnections();
        Assert.That(remaining, Does.Contain(fastPeer1));
        Assert.That(remaining, Does.Contain(fastPeer2));
        Assert.That(remaining, Does.Not.Contain(slowPeer1));
        Assert.That(remaining, Does.Not.Contain(slowPeer2));
    }

    [Test]
    public void RotateConnections_should_protect_newly_connected_peers_within_grace_period_from_eviction()
    {
        _configService.ConnectionRotationPercentage.Returns(0.5);

        // Old connection: connected 5 minutes ago (outside grace period)
        var oldConn = CreateTestConnection(DateTime.UtcNow.AddMinutes(-5));

        // New connection: connected 10 seconds ago (within grace period)
        var newConn = CreateTestConnection(DateTime.UtcNow.AddSeconds(-10));

        _manager.Add(oldConn);
        _manager.Add(newConn);

        _manager.RotateConnections();

        // New connection must be protected by the 60s grace period. Old connection is evicted.
        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
        var remaining = _manager.GetAllConnections();
        Assert.That(remaining, Does.Contain(newConn));
        Assert.That(remaining, Does.Not.Contain(oldConn));
    }

    [Test]
    public void RotateConnections_should_prioritize_snubbed_inactive_zero_speed_peers_for_eviction()
    {
        _configService.ConnectionRotationPercentage.Returns(0.34);

        var snubbedPeer = CreateTestConnection();
        snubbedPeer.IsSnubbed = true;

        var activePeer = CreateTestConnection();
        activePeer.DownloadSpeed = 10_000;
        activePeer.AmInterested = true;

        var chokedPeer = CreateTestConnection();
        chokedPeer.DownloadSpeed = 5_000;
        chokedPeer.PeerChoking = true;

        _manager.Add(snubbedPeer);
        _manager.Add(activePeer);
        _manager.Add(chokedPeer);

        _manager.RotateConnections();

        // The snubbed peer must be prioritized for eviction first
        Assert.That(_manager.ActiveCount, Is.EqualTo(2));
        var remaining = _manager.GetAllConnections();
        Assert.That(remaining, Does.Not.Contain(snubbedPeer));
        Assert.That(remaining, Does.Contain(activePeer));
        Assert.That(remaining, Does.Contain(chokedPeer));
    }

    [Test]
    public void Add_should_not_throw_when_log_service_throws_on_connect()
    {
        _connectionLogService
            .When(x => x.LogConnected(Arg.Any<PeerConnection>(), Arg.Any<string>()))
            .Do(_ => throw new Exception("Log service unavailable"));

        var conn = CreateTestConnection();

        Assert.DoesNotThrow(() => _manager.Add(conn));
    }

    [Test]
    public void Remove_should_not_throw_when_log_service_throws_on_disconnect()
    {
        _connectionLogService
            .When(x => x.LogDisconnected(Arg.Any<PeerConnection>(), Arg.Any<string>()))
            .Do(_ => throw new Exception("Log service unavailable"));

        var conn = CreateTestConnection();
        _manager.Add(conn);

        Assert.DoesNotThrow(() => _manager.Remove(conn));
    }

    [Test]
    public void Add_should_resolve_torrent_name_when_torrent_found()
    {
        var torrent = new Torrent { InfoHash = "abc123", Name = "MyTorrent" };
        _torrentService.GetByInfoHash("abc123").Returns(torrent);

        var conn = CreateTestConnection();
        SetInfoHash(conn, "abc123");
        _manager.Add(conn);

        _torrentService.DidNotReceive().GetAll();
        _connectionLogService.Received(1).LogConnected(conn, "MyTorrent");
    }

    [Test]
    public void Add_should_pass_null_name_when_torrent_service_throws()
    {
        _torrentService.GetByInfoHash(Arg.Any<string>()).Returns(x => throw new Exception("DB error"));
        _torrentService.FindByInfoHash(Arg.Any<string>()).Returns(x => throw new Exception("DB error"));

        var conn = CreateTestConnection();
        SetInfoHash(conn, "abc123");

        Assert.DoesNotThrow(() => _manager.Add(conn));
        _connectionLogService.Received(1).LogConnected(conn, null);
    }

    [Test]
    public void Add_should_pass_null_name_when_infohash_is_null()
    {
        var conn = CreateTestConnection();
        // InfoHash is null by default (no SetInfoHash call)

        _manager.Add(conn);

        _connectionLogService.Received(1).LogConnected(conn, null);
    }

    [Test]
    public void ProcessDropouts_should_dispose_removed_connections()
    {
        _configService.PeerDropoutProbability.Returns(1.0);
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.ProcessDropouts();

        Assert.That(conn.IsConnected, Is.False);
    }

    [Test]
    public void DisconnectByInfoHash_should_close_and_remove_connections_matching_info_hash()
    {
        var conn1 = CreateTestConnection();
        var conn2 = CreateTestConnection();
        var conn3 = CreateTestConnection();
        SetInfoHash(conn1, "hashA");
        SetInfoHash(conn2, "hashA");
        SetInfoHash(conn3, "hashB");
        _manager.Add(conn1);
        _manager.Add(conn2);
        _manager.Add(conn3);

        _manager.DisconnectByInfoHash("hashA");

        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
        Assert.That(_manager.GetConnections("hashA"), Is.Empty);
        Assert.That(_manager.GetConnections("hashB").Count, Is.EqualTo(1));
        Assert.That(conn1.IsConnected, Is.False);
        Assert.That(conn2.IsConnected, Is.False);
        Assert.That(conn3.IsConnected, Is.True);
        _fastExtensionHandler.Received(1).UnregisterPeer(conn1);
        _fastExtensionHandler.Received(1).UnregisterPeer(conn2);
        _fastExtensionHandler.DidNotReceive().UnregisterPeer(conn3);
        _connectionLogService.Received(1).LogDisconnected(conn1, Arg.Any<string>());
        _connectionLogService.Received(1).LogDisconnected(conn2, Arg.Any<string>());
    }

    [Test]
    public void DisconnectByInfoHash_should_be_case_insensitive()
    {
        var conn = CreateTestConnection();
        SetInfoHash(conn, "ABCDEF1234");
        _manager.Add(conn);

        _manager.DisconnectByInfoHash("abcdef1234");

        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
        Assert.That(conn.IsConnected, Is.False);
    }

    [Test]
    public void DisconnectByInfoHash_should_do_nothing_when_info_hash_is_null_or_empty()
    {
        var conn = CreateTestConnection();
        SetInfoHash(conn, "hashA");
        _manager.Add(conn);

        _manager.DisconnectByInfoHash(null);
        _manager.DisconnectByInfoHash(string.Empty);

        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
        Assert.That(conn.IsConnected, Is.True);
    }

    [Test]
    public void Handle_SeedingStoppedEvent_should_disconnect_peers_for_stopped_torrent()
    {
        var torrent = new Torrent { Id = 10, InfoHash = "torrent10hash" };
        _torrentService.Get(10).Returns(torrent);

        var conn1 = CreateTestConnection();
        var conn2 = CreateTestConnection();
        SetInfoHash(conn1, "torrent10hash");
        SetInfoHash(conn2, "otherhash");
        _manager.Add(conn1);
        _manager.Add(conn2);

        _manager.Handle(new SeedingStoppedEvent(10));

        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
        Assert.That(_manager.GetConnections("torrent10hash"), Is.Empty);
        Assert.That(conn1.IsConnected, Is.False);
        Assert.That(conn2.IsConnected, Is.True);
    }

    [Test]
    public void Handle_TorrentStatusChangedEvent_should_disconnect_peers_when_status_is_stopped_or_paused()
    {
        var torrent = new Torrent { Id = 10, InfoHash = "torrent10hash", Status = TorrentStatus.Stopped };

        var conn = CreateTestConnection();
        SetInfoHash(conn, "torrent10hash");
        _manager.Add(conn);

        _manager.Handle(new TorrentStatusChangedEvent(torrent, TorrentStatus.Seeding, TorrentStatus.Stopped));

        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
        Assert.That(conn.IsConnected, Is.False);
    }

    [Test]
    public void ResolveTorrent_should_resolve_by_info_hash_without_calling_GetAll()
    {
        var torrent = new Torrent { InfoHash = "uniquehash", Name = "UniqueTorrent" };
        _torrentService.GetByInfoHash("uniquehash").Returns(torrent);

        var conn = CreateTestConnection();
        SetInfoHash(conn, "uniquehash");
        _manager.Add(conn);

        _torrentService.DidNotReceive().GetAll();
        _connectionLogService.Received(1).LogConnected(conn, "UniqueTorrent");
    }
}
