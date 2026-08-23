using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
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

    private PeerConnection CreateTestConnection()
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

        var conn = new PeerConnection(serverClient);
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
    public void GetUploadSlotCount_should_return_config_value()
    {
        _configService.MaxUploadSlots.Returns(8);

        var result = _manager.GetUploadSlotCount();

        Assert.That(result, Is.EqualTo(8));
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
        _torrentService.GetAll().Returns(new List<Torrent> { torrent });

        var conn = CreateTestConnection();
        SetInfoHash(conn, "abc123");
        _manager.Add(conn);

        _connectionLogService.Received(1).LogConnected(conn, "MyTorrent");
    }

    [Test]
    public void Add_should_pass_null_name_when_torrent_service_throws()
    {
        _torrentService.GetAll().Returns(x => throw new Exception("DB error"));

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
}
