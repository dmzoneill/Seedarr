using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
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
    private List<PeerConnectionLog> _capturedLogs;

    [SetUp]
    public void Setup()
    {
        _repository = Substitute.For<IPeerConnectionLogRepository>();
        _service = new PeerConnectionLogService(_repository);
        _createdConnections = new List<PeerConnection>();
        _listeners = new List<TcpListener>();
        _clients = new List<TcpClient>();
        _capturedLogs = new List<PeerConnectionLog>();

        _repository.When(r => r.InsertMany(Arg.Any<IList<PeerConnectionLog>>()))
            .Do(callInfo => _capturedLogs.AddRange(callInfo.Arg<IList<PeerConnectionLog>>()));
        _repository.When(r => r.InsertMany(Arg.Any<IEnumerable<PeerConnectionLog>>()))
            .Do(callInfo => _capturedLogs.AddRange(callInfo.Arg<IEnumerable<PeerConnectionLog>>()));
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

        _service.LogConnected(conn, "test.torrent");
        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].EventType, Is.EqualTo("Connected"));
    }

    [Test]
    public void LogDisconnected_should_insert_log_with_disconnected_event_type()
    {
        var conn = CreateTestConnection();

        _service.LogDisconnected(conn, "test.torrent");
        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].EventType, Is.EqualTo("Disconnected"));
    }

    [Test]
    public void LogConnected_should_use_empty_string_for_null_info_hash()
    {
        var conn = CreateTestConnection();

        _service.LogConnected(conn, "test.torrent");
        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].InfoHash, Is.EqualTo(string.Empty));
    }

    [Test]
    public void LogConnected_should_normalize_info_hash_to_lowercase()
    {
        var conn = CreateTestConnection();
        typeof(PeerConnection).GetProperty("InfoHash")?.SetValue(conn, "AABBCCDD11223344");

        _service.LogConnected(conn, "test.torrent");
        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].InfoHash, Is.EqualTo("aabbccdd11223344"));
    }

    [Test]
    public void LogConnected_should_include_remote_ip_and_port()
    {
        var conn = CreateTestConnection();

        _service.LogConnected(conn, "test.torrent");
        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].RemoteIp, Is.EqualTo("127.0.0.1"));
        Assert.That(_capturedLogs[0].RemotePort, Is.GreaterThan(0));
    }

    [Test]
    public void LogConnected_should_include_torrent_name()
    {
        var conn = CreateTestConnection();

        _service.LogConnected(conn, "my-file.torrent");
        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].TorrentName, Is.EqualTo("my-file.torrent"));
    }

    [Test]
    public void LogConnected_should_include_encryption_status()
    {
        var conn = CreateTestConnection();

        _service.LogConnected(conn, "test.torrent");
        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].IsEncrypted, Is.False);
    }

    [Test]
    public void LogConnected_should_set_timestamp_close_to_utc_now()
    {
        var conn = CreateTestConnection();
        var before = DateTime.UtcNow;

        _service.LogConnected(conn, "test.torrent");
        _service.Flush();

        var after = DateTime.UtcNow;
        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].Timestamp, Is.GreaterThanOrEqualTo(before));
        Assert.That(_capturedLogs[0].Timestamp, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void LogConnected_should_include_peer_id()
    {
        var conn = CreateTestConnection();

        _service.LogConnected(conn, "test.torrent");
        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].PeerId, Is.Null);
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

        _repository.Received(1).Purge(before, 50000);
    }

    [Test]
    public void Purge_with_custom_cap_should_delegate_to_repository()
    {
        var before = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        _service.Purge(before, 10000);

        _repository.Received(1).Purge(before, 10000);
    }

    [Test]
    public void Dispose_should_not_throw()
    {
        Assert.DoesNotThrow(() => _service.Dispose());
    }

    [Test]
    public void LogConnected_and_LogDisconnected_writes_to_channel_and_batches_inserts_via_InsertMany()
    {
        var conn = CreateTestConnection();

        _service.LogConnected(conn, "test1.torrent");
        _service.LogDisconnected(conn, "test2.torrent");

        _repository.DidNotReceiveWithAnyArgs().Insert(Arg.Any<PeerConnectionLog>());

        _service.Flush();

        _repository.Received().InsertMany(Arg.Is<IList<PeerConnectionLog>>(list =>
            list.Count == 2 &&
            list.Any(l => l.EventType == "Connected" && l.TorrentName == "test1.torrent") &&
            list.Any(l => l.EventType == "Disconnected" && l.TorrentName == "test2.torrent")));
    }

    [Test]
    public void Batch_draining_chunks_large_volume_into_batches_of_100()
    {
        var conn = CreateTestConnection();
        var capturedBatches = new List<List<PeerConnectionLog>>();
        _repository.When(r => r.InsertMany(Arg.Any<IList<PeerConnectionLog>>()))
            .Do(callInfo => capturedBatches.Add(callInfo.Arg<IList<PeerConnectionLog>>().ToList()));

        for (var i = 1; i <= 150; i++)
        {
            _service.LogConnected(conn, $"torrent-{i}.torrent");
        }

        _service.Flush();

        Assert.That(capturedBatches.Sum(b => b.Count), Is.EqualTo(150));
        Assert.That(capturedBatches.All(b => b.Count <= 100), Is.True);
    }

    [Test]
    public void Flush_drains_all_pending_connection_logs()
    {
        var conn = CreateTestConnection();
        for (var i = 1; i <= 5; i++)
        {
            _service.LogConnected(conn, $"pending-{i}.torrent");
        }

        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(5));
    }

    [Test]
    public void Dispose_drains_all_pending_connection_logs_before_completing()
    {
        var conn = CreateTestConnection();
        for (var i = 1; i <= 5; i++)
        {
            _service.LogConnected(conn, $"dispose-{i}.torrent");
        }

        _service.Dispose();

        Assert.That(_capturedLogs, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task DisposeAsync_drains_all_pending_connection_logs_before_completing()
    {
        var conn = CreateTestConnection();
        for (var i = 1; i <= 10; i++)
        {
            _service.LogConnected(conn, $"async-dispose-{i}.torrent");
        }

        await _service.DisposeAsync();

        Assert.That(_capturedLogs, Has.Count.EqualTo(10));
    }

    [Test]
    public void LogDisconnected_should_include_torrent_name()
    {
        var conn = CreateTestConnection();

        _service.LogDisconnected(conn, "another.torrent");
        _service.Flush();

        Assert.That(_capturedLogs, Has.Count.EqualTo(1));
        Assert.That(_capturedLogs[0].TorrentName, Is.EqualTo("another.torrent"));
    }

    [Test]
    public void GetConnectionCounts_should_delegate_to_repository()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        _repository.GetConnectionCounts(start, end).Returns((42, 10));

        var result = _service.GetConnectionCounts(start, end);

        Assert.That(result.EncryptedCount, Is.EqualTo(42));
        Assert.That(result.PlaintextCount, Is.EqualTo(10));
        _repository.Received(1).GetConnectionCounts(start, end);
    }
}
