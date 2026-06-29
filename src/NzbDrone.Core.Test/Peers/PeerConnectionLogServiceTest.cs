using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class PeerConnectionLogServiceTest
{
    private IPeerConnectionLogRepository _repository;
    private PeerConnectionLogService _service;
    private List<PeerConnection> _createdConnections;
    private List<TcpListener> _listeners;
    private List<TcpClient> _clients;

    [SetUp]
    public void Setup()
    {
        _repository = Substitute.For<IPeerConnectionLogRepository>();
        _service = new PeerConnectionLogService(_repository);
        _createdConnections = new List<PeerConnection>();
        _listeners = new List<TcpListener>();
        _clients = new List<TcpClient>();
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();

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

        foreach (var client in _clients)
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
        _clients.Add(client);

        var serverClient = listener.AcceptTcpClient();
        _clients.Add(serverClient);
        listener.Stop();

        var conn = new PeerConnection(serverClient);
        _createdConnections.Add(conn);
        return conn;
    }

    [Test]
    public void LogConnected_should_insert_log_with_connected_event_type()
    {
        var conn = CreateTestConnection();
        PeerConnectionLog capturedLog = null;
        _repository.Insert(Arg.Do<PeerConnectionLog>(log => capturedLog = log));

        _service.LogConnected(conn, "test.torrent");

        Assert.That(capturedLog, Is.Not.Null);
        Assert.That(capturedLog.EventType, Is.EqualTo("Connected"));
    }

    [Test]
    public void LogDisconnected_should_insert_log_with_disconnected_event_type()
    {
        var conn = CreateTestConnection();
        PeerConnectionLog capturedLog = null;
        _repository.Insert(Arg.Do<PeerConnectionLog>(log => capturedLog = log));

        _service.LogDisconnected(conn, "test.torrent");

        Assert.That(capturedLog, Is.Not.Null);
        Assert.That(capturedLog.EventType, Is.EqualTo("Disconnected"));
    }

    [Test]
    public void LogConnected_should_use_empty_string_for_null_info_hash()
    {
        var conn = CreateTestConnection();
        PeerConnectionLog capturedLog = null;
        _repository.Insert(Arg.Do<PeerConnectionLog>(log => capturedLog = log));

        _service.LogConnected(conn, "test.torrent");

        Assert.That(capturedLog.InfoHash, Is.EqualTo(string.Empty));
    }

    [Test]
    public void LogConnected_should_include_remote_ip_and_port()
    {
        var conn = CreateTestConnection();
        PeerConnectionLog capturedLog = null;
        _repository.Insert(Arg.Do<PeerConnectionLog>(log => capturedLog = log));

        _service.LogConnected(conn, "test.torrent");

        Assert.That(capturedLog.RemoteIp, Is.EqualTo("127.0.0.1"));
        Assert.That(capturedLog.RemotePort, Is.GreaterThan(0));
    }

    [Test]
    public void LogConnected_should_include_torrent_name()
    {
        var conn = CreateTestConnection();
        PeerConnectionLog capturedLog = null;
        _repository.Insert(Arg.Do<PeerConnectionLog>(log => capturedLog = log));

        _service.LogConnected(conn, "my-file.torrent");

        Assert.That(capturedLog.TorrentName, Is.EqualTo("my-file.torrent"));
    }

    [Test]
    public void LogConnected_should_include_encryption_status()
    {
        var conn = CreateTestConnection();
        PeerConnectionLog capturedLog = null;
        _repository.Insert(Arg.Do<PeerConnectionLog>(log => capturedLog = log));

        _service.LogConnected(conn, "test.torrent");

        Assert.That(capturedLog.IsEncrypted, Is.False);
    }

    [Test]
    public void LogConnected_should_set_timestamp_close_to_utc_now()
    {
        var conn = CreateTestConnection();
        PeerConnectionLog capturedLog = null;
        _repository.Insert(Arg.Do<PeerConnectionLog>(log => capturedLog = log));
        var before = DateTime.UtcNow;

        _service.LogConnected(conn, "test.torrent");

        var after = DateTime.UtcNow;
        Assert.That(capturedLog.Timestamp, Is.GreaterThanOrEqualTo(before));
        Assert.That(capturedLog.Timestamp, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void LogConnected_should_include_peer_id()
    {
        var conn = CreateTestConnection();
        PeerConnectionLog capturedLog = null;
        _repository.Insert(Arg.Do<PeerConnectionLog>(log => capturedLog = log));

        _service.LogConnected(conn, "test.torrent");

        Assert.That(capturedLog.PeerId, Is.Null);
    }

    [Test]
    public void GetByTimeRange_should_delegate_to_repository()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var expected = new List<PeerConnectionLog> { new PeerConnectionLog { InfoHash = "abc" } };
        _repository.GetByTimeRange(start, end).Returns(expected);

        var result = _service.GetByTimeRange(start, end);

        Assert.That(result, Is.SameAs(expected));
        _repository.Received(1).GetByTimeRange(start, end);
    }

    [Test]
    public void GetByInfoHash_should_delegate_to_repository()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var expected = new List<PeerConnectionLog> { new PeerConnectionLog { InfoHash = "abc123" } };
        _repository.GetByInfoHash("abc123", start, end).Returns(expected);

        var result = _service.GetByInfoHash("abc123", start, end);

        Assert.That(result, Is.SameAs(expected));
        _repository.Received(1).GetByInfoHash("abc123", start, end);
    }

    [Test]
    public void Purge_should_delegate_to_repository()
    {
        var before = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        _service.Purge(before);

        _repository.Received(1).Purge(before);
    }

    [Test]
    public void Dispose_should_not_throw()
    {
        Assert.DoesNotThrow(() => _service.Dispose());
    }

    [Test]
    public void LogConnected_should_call_repository_insert_exactly_once()
    {
        var conn = CreateTestConnection();

        _service.LogConnected(conn, "test.torrent");

        _repository.Received(1).Insert(Arg.Any<PeerConnectionLog>());
    }

    [Test]
    public void LogDisconnected_should_include_torrent_name()
    {
        var conn = CreateTestConnection();
        PeerConnectionLog capturedLog = null;
        _repository.Insert(Arg.Do<PeerConnectionLog>(log => capturedLog = log));

        _service.LogDisconnected(conn, "another.torrent");

        Assert.That(capturedLog.TorrentName, Is.EqualTo("another.torrent"));
    }
}
