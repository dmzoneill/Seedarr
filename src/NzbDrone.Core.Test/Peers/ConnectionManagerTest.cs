using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
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

    [Test]
    public void TryReserveSlot_should_enforce_MaxPerTorrentConnections_limit()
    {
        _configService.MaxPerTorrentConnections.Returns(2);
        _configService.MaxGlobalConnections.Returns(10);

        Assert.That(_manager.TryReserveSlot("hashA", false, out var res1), Is.True);
        Assert.That(res1, Is.Not.Null);
        Assert.That(res1.InfoHash, Is.EqualTo("hashA"));
        Assert.That(res1.IsInbound, Is.False);

        Assert.That(_manager.TryReserveSlot("hashA", true, out var res2), Is.True);
        Assert.That(res2, Is.Not.Null);

        // 3rd attempt should fail because limit is 2
        Assert.That(_manager.TryReserveSlot("hashA", false, out var res3), Is.False);
        Assert.That(res3, Is.Null);

        // Another torrent should still be able to reserve slots
        Assert.That(_manager.TryReserveSlot("hashB", false, out var resB), Is.True);
        Assert.That(resB, Is.Not.Null);
    }

    [Test]
    public void TryReserveSlot_should_enforce_MaxGlobalConnections_limit()
    {
        _configService.MaxPerTorrentConnections.Returns(10);
        _configService.MaxGlobalConnections.Returns(2);

        Assert.That(_manager.TryReserveSlot("hashA", false, out var res1), Is.True);
        Assert.That(_manager.TryReserveSlot("hashB", false, out var res2), Is.True);

        // 3rd attempt globally should fail
        Assert.That(_manager.TryReserveSlot("hashC", false, out var res3), Is.False);
        Assert.That(res3, Is.Null);
    }

    [Test]
    public void TryReserveSlot_should_automatically_release_slots_when_reservation_expires()
    {
        _configService.MaxPerTorrentConnections.Returns(1);
        _configService.MaxGlobalConnections.Returns(10);

        // Reserve with expired TTL
        Assert.That(_manager.TryReserveSlot("hashA", false, TimeSpan.FromMilliseconds(-1), out var res1), Is.True);

        // Subsequent reservation should succeed because the expired reservation is pruned
        Assert.That(_manager.TryReserveSlot("hashA", false, out var res2), Is.True);
        Assert.That(res2, Is.Not.Null);
    }

    [Test]
    public void TryReserveSlot_disposing_reservation_should_release_slot()
    {
        _configService.MaxPerTorrentConnections.Returns(1);
        _configService.MaxGlobalConnections.Returns(10);

        Assert.That(_manager.TryReserveSlot("hashA", false, out var res), Is.True);
        Assert.That(_manager.TryReserveSlot("hashA", false, out _), Is.False);

        res.Dispose();

        Assert.That(_manager.TryReserveSlot("hashA", false, out var res2), Is.True);
        Assert.That(res2, Is.Not.Null);
    }

    [Test]
    public void TryAdd_should_fulfill_reservation_and_add_connection()
    {
        _configService.MaxPerTorrentConnections.Returns(1);
        _configService.MaxGlobalConnections.Returns(10);

        Assert.That(_manager.TryReserveSlot("hashA", false, out var res), Is.True);
        Assert.That(_manager.TryReserveSlot("hashA", false, out _), Is.False);

        var conn = CreateTestConnection();
        SetInfoHash(conn, "hashA");

        Assert.That(_manager.TryAdd(conn, res), Is.True);
        Assert.That(_manager.ActiveCount, Is.EqualTo(1));

        // Slot is now occupied by active connection, so another reservation still fails
        Assert.That(_manager.TryReserveSlot("hashA", false, out _), Is.False);

        // And disposing the fulfilled reservation does not remove the active connection
        res.Dispose();
        Assert.That(_manager.ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public void Add_should_evict_lowest_scoring_peer_from_same_torrent_when_MaxPerTorrentConnections_reached()
    {
        _configService.MaxPerTorrentConnections.Returns(2);
        _configService.MaxGlobalConnections.Returns(10);

        var conn1 = CreateTestConnection();
        var conn2 = CreateTestConnection();
        var conn3 = CreateTestConnection();
        SetInfoHash(conn1, "hashA");
        SetInfoHash(conn2, "hashA");
        SetInfoHash(conn3, "hashA");

        _manager.Add(conn1);
        _manager.Add(conn2);
        Assert.That(_manager.ActiveCount, Is.EqualTo(2));

        _manager.Add(conn3);
        Assert.That(_manager.ActiveCount, Is.EqualTo(2));
        Assert.That(_manager.GetConnections("hashA").Count, Is.EqualTo(2));
    }

    [TestCase(0, 0)]
    [TestCase(-1, -1)]
    [TestCase(0, 50)]
    [TestCase(-1, 50)]
    public void Add_when_MaxGlobalConnections_is_zero_or_negative_allows_unlimited_connections_without_eviction(int maxGlobal, int maxPerTorrent)
    {
        _configService.MaxGlobalConnections.Returns(maxGlobal);
        _configService.MaxPerTorrentConnections.Returns(maxPerTorrent);

        for (var i = 1; i <= 10; i++)
        {
            var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 2000 + i) { InfoHash = "hash1" };
            _createdConnections.Add(conn);
            Assert.DoesNotThrow(() => _manager.Add(conn));
        }

        Assert.That(_manager.ActiveCount, Is.EqualTo(10));
        Assert.That(_manager.Count, Is.EqualTo(10));
        _fastExtensionHandler.DidNotReceive().UnregisterPeer(Arg.Any<PeerConnection>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void CanAddConnectionForTorrent_when_MaxPerTorrentConnections_is_zero_or_negative_returns_true(int maxPerTorrent)
    {
        _configService.MaxPerTorrentConnections.Returns(maxPerTorrent);
        _configService.MaxGlobalConnections.Returns(100);

        for (var i = 1; i <= 5; i++)
        {
            var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 3000 + i) { InfoHash = "hashA" };
            _createdConnections.Add(conn);
            _manager.Add(conn);
        }

        Assert.That(_manager.CanAddConnectionForTorrent("hashA"), Is.True);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void CanAddConnectionForTorrent_when_MaxGlobalConnections_is_zero_or_negative_returns_true(int maxGlobal)
    {
        _configService.MaxGlobalConnections.Returns(maxGlobal);
        _configService.MaxPerTorrentConnections.Returns(100);

        for (var i = 1; i <= 5; i++)
        {
            var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", 4000 + i) { InfoHash = "hashB" };
            _createdConnections.Add(conn);
            _manager.Add(conn);
        }

        Assert.That(_manager.CanAddConnectionForTorrent("hashB"), Is.True);
    }

    [TestCase(0, 0)]
    [TestCase(-1, -1)]
    public void TryReserveSlot_when_limits_are_zero_or_negative_allows_reservations(int maxGlobal, int maxPerTorrent)
    {
        _configService.MaxGlobalConnections.Returns(maxGlobal);
        _configService.MaxPerTorrentConnections.Returns(maxPerTorrent);

        for (var i = 1; i <= 5; i++)
        {
            Assert.That(_manager.TryReserveSlot("hashUnlimited", false, out var res), Is.True);
            Assert.That(res, Is.Not.Null);
        }
    }

    [Test]
    public void Add_when_torrent_limit_reached_evicts_from_same_torrent_without_evicting_other_torrents()
    {
        _configService.MaxPerTorrentConnections.Returns(2);
        _configService.MaxGlobalConnections.Returns(10);

        var connA1 = new PeerConnection(new MemoryStream(), "127.0.0.1", 5001) { InfoHash = "hashA", ConnectedAt = DateTime.UtcNow.AddMinutes(-10), LastActivity = DateTime.UtcNow.AddMinutes(-10) };
        var connA2 = new PeerConnection(new MemoryStream(), "127.0.0.1", 5002) { InfoHash = "hashA", ConnectedAt = DateTime.UtcNow.AddMinutes(-5), LastActivity = DateTime.UtcNow.AddMinutes(-5) };
        var connB1 = new PeerConnection(new MemoryStream(), "127.0.0.1", 5003) { InfoHash = "hashB", ConnectedAt = DateTime.UtcNow.AddMinutes(-2), LastActivity = DateTime.UtcNow.AddMinutes(-2) };
        _createdConnections.AddRange(new[] { connA1, connA2, connB1 });

        _manager.Add(connA1);
        _manager.Add(connA2);
        _manager.Add(connB1);

        Assert.That(_manager.ActiveCount, Is.EqualTo(3));
        Assert.That(_manager.GetConnections("hashA").Count, Is.EqualTo(2));
        Assert.That(_manager.GetConnections("hashB").Count, Is.EqualTo(1));

        var connA3 = new PeerConnection(new MemoryStream(), "127.0.0.1", 5004) { InfoHash = "hashA", ConnectedAt = DateTime.UtcNow, LastActivity = DateTime.UtcNow };
        _createdConnections.Add(connA3);

        _manager.Add(connA3);

        Assert.That(_manager.ActiveCount, Is.EqualTo(3));
        Assert.That(_manager.GetConnections("hashA").Count, Is.EqualTo(2));
        var hashBConnections = _manager.GetConnections("hashB");
        Assert.That(hashBConnections.Count, Is.EqualTo(1));
        Assert.That(hashBConnections[0], Is.SameAs(connB1));
        _fastExtensionHandler.Received(1).UnregisterPeer(connA1);
        _fastExtensionHandler.DidNotReceive().UnregisterPeer(connB1);
    }

    [Test]
    public void Concurrent_enumerations_and_modifications_do_not_throw_InvalidOperationException()
    {
        _configService.MaxGlobalConnections.Returns(50);
        _configService.MaxPerTorrentConnections.Returns(10);

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(2));
        var token = cancellationTokenSource.Token;

        var exceptions = new ConcurrentBag<Exception>();

        for (var i = 1; i <= 20; i++)
        {
            var c = new PeerConnection(new MemoryStream(), "127.0.0.1", 6000 + i) { InfoHash = "hash" + (i % 3) };
            _createdConnections.Add(c);
            _manager.Add(c);
        }

        var tasks = new List<Task>();

        tasks.Add(Task.Run(() =>
        {
            var counter = 7000;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var id = Interlocked.Increment(ref counter);
                    var conn = new PeerConnection(new MemoryStream(), "127.0.0.1", (id % 60000) + 1024)
                    {
                        InfoHash = "hash" + (id % 3)
                    };
                    _manager.Add(conn);
                    _manager.Remove(conn);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }));

        tasks.Add(Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var all1 = _manager.GetAll();
                    foreach (var c in all1)
                    {
                        _ = c.RemotePort;
                    }

                    var all2 = _manager.GetAllConnections();
                    foreach (var c in all2)
                    {
                        _ = c.RemotePort;
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }));

        tasks.Add(Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var tor1 = _manager.GetForTorrent("hash0");
                    foreach (var c in tor1)
                    {
                        _ = c.RemotePort;
                    }

                    var tor2 = _manager.GetConnections("hash1");
                    foreach (var c in tor2)
                    {
                        _ = c.RemotePort;
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }));

        tasks.Add(Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _ = _manager.Count;
                    _ = _manager.ActiveCount;
                    _ = _manager.CanAddConnectionForTorrent("hash0");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        }));

        Task.WaitAll(tasks.ToArray());

        Assert.That(exceptions, Is.Empty, $"Exceptions encountered during concurrent operations: {string.Join(", ", exceptions.Select(e => e.ToString()))}");
    }

    [Test]
    public void GetAll_and_GetForTorrent_and_Count_should_return_consistent_values()
    {
        var conn1 = new PeerConnection(new MemoryStream(), "127.0.0.1", 8001) { InfoHash = "hashAlpha" };
        var conn2 = new PeerConnection(new MemoryStream(), "127.0.0.1", 8002) { InfoHash = "hashBeta" };
        _createdConnections.Add(conn1);
        _createdConnections.Add(conn2);

        _manager.Add(conn1);
        _manager.Add(conn2);

        Assert.That(_manager.Count, Is.EqualTo(2));
        Assert.That(_manager.ActiveCount, Is.EqualTo(2));
        Assert.That(_manager.GetAll().Count, Is.EqualTo(2));
        Assert.That(_manager.GetForTorrent("hashAlpha").Count, Is.EqualTo(1));
        Assert.That(_manager.GetForTorrent("hashAlpha")[0], Is.SameAs(conn1));
        Assert.That(_manager.GetForTorrent("hashBeta").Count, Is.EqualTo(1));
        Assert.That(_manager.GetForTorrent("hashBeta")[0], Is.SameAs(conn2));
    }

    [Test]
    public void BanPeer_should_track_peer_as_banned_with_ttl()
    {
        _manager.BanPeer("192.168.1.100", TimeSpan.FromMinutes(10));

        Assert.That(_manager.IsPeerBanned("192.168.1.100"), Is.True);
        Assert.That(_manager.IsPeerBanned("192.168.1.101"), Is.False);
        Assert.That(_manager.GetBannedPeers(), Does.Contain("192.168.1.100"));
    }

    [Test]
    public void BanPeer_should_forcibly_disconnect_and_remove_active_connections_for_ip()
    {
        var conn = CreateTestConnection();
        _manager.Add(conn);

        Assert.That(_manager.ActiveCount, Is.EqualTo(1));

        _manager.BanPeer(conn.RemoteIp);

        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
        _connectionLogService.Received(1).LogDisconnected(conn, Arg.Any<string>());
    }

    [Test]
    public void TryAdd_should_reject_connection_from_banned_peer()
    {
        var conn = CreateTestConnection();
        _manager.BanPeer(conn.RemoteIp);

        var added = _manager.TryAdd(conn, null);

        Assert.That(added, Is.False);
        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void IsPeerBanned_should_return_false_after_ttl_expires()
    {
        _manager.BanPeer("192.168.1.200", TimeSpan.FromMilliseconds(-1));

        Assert.That(_manager.IsPeerBanned("192.168.1.200"), Is.False);
    }

    [Test]
    public void Handle_PeerBannedEvent_should_ban_peer_and_disconnect_connections()
    {
        var conn = CreateTestConnection();
        _manager.Add(conn);

        _manager.Handle(new PeerBannedEvent(conn.RemoteIp, "Banned by automation"));

        Assert.That(_manager.IsPeerBanned(conn.RemoteIp), Is.True);
        Assert.That(_manager.ActiveCount, Is.EqualTo(0));
    }

    [Test]
    public void BanPeer_should_support_cidr_subnets()
    {
        _manager.BanPeer("10.0.0.0/24");

        Assert.That(_manager.IsPeerBanned("10.0.0.50"), Is.True);
        Assert.That(_manager.IsPeerBanned("10.0.1.50"), Is.False);
    }

    [Test]
    public void UnbanPeer_should_remove_ban()
    {
        _manager.BanPeer("192.168.1.50");
        Assert.That(_manager.IsPeerBanned("192.168.1.50"), Is.True);

        _manager.UnbanPeer("192.168.1.50");
        Assert.That(_manager.IsPeerBanned("192.168.1.50"), Is.False);
    }
}
