using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;
using Seedarr.Api.V1.Peers;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class PeerConnectionLogControllerTest
{
    private IPeerConnectionLogService _logService;
    private IConnectionManager _connectionManager;
    private ITorrentService _torrentService;
    private IPeerDatabase _peerDatabase;
    private PeerConnectionLogController _controller;

    [SetUp]
    public void SetUp()
    {
        _logService = Substitute.For<IPeerConnectionLogService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _torrentService = Substitute.For<ITorrentService>();
        _peerDatabase = Substitute.For<IPeerDatabase>();

        _controller = new PeerConnectionLogController(
            _logService,
            _connectionManager,
            _torrentService,
            _peerDatabase);
    }

    [Test]
    public void GetLogs_when_start_is_after_end_returns_bad_request()
    {
        var now = DateTime.UtcNow;
        var start = now;
        var end = now.AddHours(-1);

        var result = _controller.GetLogs(start, end, null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Start date must be earlier than or equal to end date."));
    }

    [Test]
    public void GetLogs_when_window_exceeds_seven_days_returns_bad_request()
    {
        var now = DateTime.UtcNow;
        var start = now.AddDays(-8);
        var end = now;

        var result = _controller.GetLogs(start, end, null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Requested time window cannot exceed 7 days."));
    }

    [Test]
    public void GetGraph_when_start_is_after_end_returns_bad_request()
    {
        var now = DateTime.UtcNow;
        var start = now;
        var end = now.AddHours(-2);

        var result = _controller.GetGraph(start, end);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Start date must be earlier than or equal to end date."));
    }

    [Test]
    public void GetGraph_when_window_exceeds_seven_days_returns_bad_request()
    {
        var now = DateTime.UtcNow;
        var start = now.AddDays(-7).AddMinutes(-5);
        var end = now;

        var result = _controller.GetGraph(start, end);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("Requested time window cannot exceed 7 days."));
    }

    [Test]
    public void GetGraph_reconciles_disconnect_events_and_excludes_disconnected_peers()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-1);
        var end = now;

        var logs = new List<PeerConnectionLog>
        {
            new PeerConnectionLog
            {
                Id = 1,
                RemoteIp = "192.168.1.10",
                RemotePort = 5000,
                InfoHash = "aabbccdd11223344",
                TorrentName = "TestTorrent",
                EventType = "Connected",
                Timestamp = now.AddMinutes(-30)
            },
            new PeerConnectionLog
            {
                Id = 2,
                RemoteIp = "192.168.1.10",
                RemotePort = 5000,
                InfoHash = "aabbccdd11223344",
                TorrentName = "TestTorrent",
                EventType = "Disconnected",
                Timestamp = now.AddMinutes(-25)
            },
            new PeerConnectionLog
            {
                Id = 3,
                RemoteIp = "192.168.1.20",
                RemotePort = 6000,
                InfoHash = "aabbccdd11223344",
                TorrentName = "TestTorrent",
                EventType = "Connected",
                Timestamp = now.AddMinutes(-20)
            }
        };

        _logService.GetByTimeRange(start, end).Returns(logs);
        _torrentService.GetAll().Returns(new List<Torrent>());
        _peerDatabase.GetAllInfoHashes().Returns(new List<string>());

        var result = _controller.GetGraph(start, end);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var graph = (PeerGraphResource)okResult.Value;

        var hasDisconnectedNode = graph.Nodes.Any(n => n.Id == "peer:192.168.1.10:5000:aabbccdd11223344");
        var hasConnectedNode = graph.Nodes.Any(n => n.Id == "peer:192.168.1.20:6000:aabbccdd11223344");
        var hasDisconnectedLink = graph.Links.Any(l => l.Target == "peer:192.168.1.10:5000:aabbccdd11223344");

        Assert.That(hasDisconnectedNode, Is.False, "Disconnected peer should not be in the graph nodes");
        Assert.That(hasConnectedNode, Is.True, "Active connected peer should be in the graph nodes");
        Assert.That(hasDisconnectedLink, Is.False, "Disconnected peer should not have links in the graph");
    }

    [Test]
    public void GetGraph_reconnect_after_disconnect_renders_peer_as_active()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-1);
        var end = now;

        var logs = new List<PeerConnectionLog>
        {
            new PeerConnectionLog
            {
                Id = 1,
                RemoteIp = "192.168.1.10",
                RemotePort = 5000,
                InfoHash = "aabbccdd11223344",
                TorrentName = "TestTorrent",
                EventType = "Connected",
                Timestamp = now.AddMinutes(-30)
            },
            new PeerConnectionLog
            {
                Id = 2,
                RemoteIp = "192.168.1.10",
                RemotePort = 5000,
                InfoHash = "aabbccdd11223344",
                TorrentName = "TestTorrent",
                EventType = "Disconnected",
                Timestamp = now.AddMinutes(-20)
            },
            new PeerConnectionLog
            {
                Id = 3,
                RemoteIp = "192.168.1.10",
                RemotePort = 5000,
                InfoHash = "aabbccdd11223344",
                TorrentName = "TestTorrent",
                EventType = "Connected",
                Timestamp = now.AddMinutes(-10)
            }
        };

        _logService.GetByTimeRange(start, end).Returns(logs);
        _torrentService.GetAll().Returns(new List<Torrent>());
        _peerDatabase.GetAllInfoHashes().Returns(new List<string>());

        var result = _controller.GetGraph(start, end);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var graph = (PeerGraphResource)okResult.Value;

        var hasReconnectedNode = graph.Nodes.Any(n => n.Id == "peer:192.168.1.10:5000:aabbccdd11223344");
        Assert.That(hasReconnectedNode, Is.True, "Reconnected peer should be rendered as active");
    }

    [Test]
    public void GetGraph_disconnected_peer_in_tracker_database_is_not_resurrected()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-1);
        var end = now;
        const string hash = "aabbccdd11223344";

        var logs = new List<PeerConnectionLog>
        {
            new PeerConnectionLog
            {
                Id = 1,
                RemoteIp = "192.168.1.10",
                RemotePort = 5000,
                InfoHash = hash,
                EventType = "Disconnected",
                Timestamp = now.AddMinutes(-10)
            }
        };

        _logService.GetByTimeRange(start, end).Returns(logs);
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new Torrent { InfoHash = hash, Name = "Torrent" }
        });
        _peerDatabase.GetPeers(hash).Returns(new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "192.168.1.10", Port = 5000 }
        });
        _peerDatabase.GetAllInfoHashes().Returns(new List<string> { hash });

        var result = _controller.GetGraph(start, end);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var graph = (PeerGraphResource)okResult.Value;

        var hasResurrectedNode = graph.Nodes.Any(n => n.Id == $"peer:192.168.1.10:5000:{hash}");
        Assert.That(hasResurrectedNode, Is.False, "Peer that explicitly disconnected should not be resurrected by tracker database");
    }

    [Test]
    public void GetActive_queries_connection_manager_and_maps_active_connections()
    {
        var conn = CreateMockPeerConnection("192.168.1.50", 6881, "aabbccdd11223344", "-TR3000-abcdef123456", true);
        _connectionManager.GetAllConnections().Returns(new List<PeerConnection> { conn });

        var result = _controller.GetActive();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var resources = (List<PeerConnectionLogResource>)okResult.Value;

        Assert.That(resources.Count, Is.EqualTo(1));
        Assert.That(resources[0].InfoHash, Is.EqualTo("aabbccdd11223344"));
        Assert.That(resources[0].RemoteIp, Is.EqualTo("192.168.1.50"));
        Assert.That(resources[0].RemotePort, Is.EqualTo(6881));
        Assert.That(resources[0].PeerId, Is.EqualTo("-TR3000-abcdef123456"));
        Assert.That(resources[0].IsEncrypted, Is.True);
        Assert.That(resources[0].EventType, Is.EqualTo("Connected"));
        Assert.That(resources[0].Timestamp, Is.EqualTo(conn.ConnectedAt));

        _connectionManager.Received(1).GetAllConnections();
    }

    [Test]
    public void GetActive_does_not_execute_database_table_scan()
    {
        var conn = CreateMockPeerConnection("10.0.0.1", 51413, "hash123", "peer123", false);
        _connectionManager.GetAllConnections().Returns(new List<PeerConnection> { conn });

        var result = _controller.GetActive();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _logService.DidNotReceiveWithAnyArgs().GetByTimeRange(default, default);
        _logService.DidNotReceiveWithAnyArgs().GetByInfoHash(default, default, default);
    }

    [Test]
    public void GetActive_when_connection_manager_is_null_returns_empty_list()
    {
        var controllerWithoutManager = new PeerConnectionLogController(_logService, null);

        var result = controllerWithoutManager.GetActive();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var resources = (List<PeerConnectionLogResource>)okResult.Value;

        Assert.That(resources, Is.Empty);
    }

    [Test]
    public void GetGraph_mixed_and_uppercase_infohashes_produce_lowercase_node_ids_and_links()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-1);
        var end = now;
        const string upperHash = "AABBCCDD11223344";
        const string lowerHash = "aabbccdd11223344";

        var logs = new List<PeerConnectionLog>
        {
            new PeerConnectionLog
            {
                Id = 1,
                RemoteIp = "192.168.1.10",
                RemotePort = 5000,
                InfoHash = upperHash,
                TorrentName = "MixedTorrent",
                EventType = "Connected",
                Timestamp = now.AddMinutes(-30),
            },
        };

        _logService.GetByTimeRange(start, end).Returns(logs);
        _torrentService.GetAll().Returns(new List<Torrent>
        {
            new Torrent { InfoHash = "AaBbCcDd11223344", Name = "MixedTorrent" },
        });
        _peerDatabase.GetPeers(lowerHash).Returns(new List<TrackerPeerEntry>
        {
            new TrackerPeerEntry { Ip = "192.168.1.50", Port = 6000 },
        });
        _peerDatabase.GetAllInfoHashes().Returns(new List<string> { upperHash });

        var result = _controller.GetGraph(start, end);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var graph = (PeerGraphResource)okResult.Value;

        var torrentNodes = graph.Nodes.Where(n => n.Type == "torrent").ToList();
        Assert.That(torrentNodes.Count, Is.EqualTo(1));
        Assert.That(torrentNodes[0].Id, Is.EqualTo($"torrent:{lowerHash}"));
        Assert.That(torrentNodes[0].InfoHash, Is.EqualTo(lowerHash));

        var seedsLink = graph.Links.FirstOrDefault(l => l.Source == "seedarr" && l.Target == $"torrent:{lowerHash}");
        Assert.That(seedsLink, Is.Not.Null);

        var peerLink = graph.Links.FirstOrDefault(l => l.Source == $"torrent:{lowerHash}");
        Assert.That(peerLink, Is.Not.Null);
        Assert.That(peerLink.Target, Does.StartWith("peer:"));
        Assert.That(peerLink.Target, Does.EndWith($":{lowerHash}"));

        foreach (var node in graph.Nodes)
        {
            Assert.That(node.Id, Is.EqualTo(node.Id.ToLowerInvariant()));
        }

        foreach (var link in graph.Links)
        {
            Assert.That(link.Source, Is.EqualTo(link.Source.ToLowerInvariant()));
            Assert.That(link.Target, Is.EqualTo(link.Target.ToLowerInvariant()));
        }
    }

    [Test]
    public void GetGraph_referential_integrity_ensures_all_link_endpoints_exist_in_nodes()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-1);
        var end = now;

        _logService.GetByTimeRange(start, end).Returns(new List<PeerConnectionLog>());
        _torrentService.GetAll().Returns(new List<Torrent>());
        _peerDatabase.GetAllInfoHashes().Returns(new List<string>());

        var result = _controller.GetGraph(start, end);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var graph = (PeerGraphResource)okResult.Value;

        var nodeIds = new HashSet<string>(graph.Nodes.Select(n => n.Id));
        foreach (var link in graph.Links)
        {
            Assert.That(nodeIds.Contains(link.Source), Is.True, $"Link Source '{link.Source}' must exist in nodes");
            Assert.That(nodeIds.Contains(link.Target), Is.True, $"Link Target '{link.Target}' must exist in nodes");
        }
    }

    [Test]
    public void GetGraph_empty_or_null_infohash_logs_are_not_linked_to_root_hub()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-1);
        var end = now;

        var logs = new List<PeerConnectionLog>
        {
            new PeerConnectionLog
            {
                Id = 1,
                RemoteIp = "192.168.1.99",
                RemotePort = 5555,
                InfoHash = null,
                EventType = "Connected",
                Timestamp = now.AddMinutes(-10),
            },
            new PeerConnectionLog
            {
                Id = 2,
                RemoteIp = "192.168.1.98",
                RemotePort = 5556,
                InfoHash = "   ",
                EventType = "Connected",
                Timestamp = now.AddMinutes(-5),
            },
        };

        _logService.GetByTimeRange(start, end).Returns(logs);
        _torrentService.GetAll().Returns(new List<Torrent>());
        _peerDatabase.GetAllInfoHashes().Returns(new List<string>());

        var result = _controller.GetGraph(start, end);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var graph = (PeerGraphResource)okResult.Value;

        Assert.That(graph.Nodes.Count, Is.EqualTo(1));
        Assert.That(graph.Nodes[0].Id, Is.EqualTo("seedarr"));
        Assert.That(graph.Links, Is.Empty);
    }

    private static PeerConnection CreateMockPeerConnection(string ip, int port, string infoHash, string peerId, bool isEncrypted)
    {
        var conn = new PeerConnection(new MemoryStream(), ip, port);
        typeof(PeerConnection).GetProperty("InfoHash")?.SetValue(conn, infoHash);
        typeof(PeerConnection).GetProperty("PeerId")?.SetValue(conn, peerId);
        typeof(PeerConnection).GetProperty("IsEncrypted")?.SetValue(conn, isEncrypted);
        return conn;
    }

    [Test]
    public void GetLogs_passes_limit_and_offset_to_log_service()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-1);
        var end = now;

        _logService.GetByTimeRange(start, end, 50, 10).Returns(new List<PeerConnectionLog>());

        var result = _controller.GetLogs(start, end, null, limit: 50, offset: 10);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _logService.Received(1).GetByTimeRange(start, end, 50, 10);
    }

    [Test]
    public void GetLogs_with_infohash_passes_limit_and_offset_to_log_service()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-1);
        var end = now;

        _logService.GetByInfoHash("aabbcc", start, end, 25, 5).Returns(new List<PeerConnectionLog>());

        var result = _controller.GetLogs(start, end, "AABBCC", limit: 25, offset: 5);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _logService.Received(1).GetByInfoHash("aabbcc", start, end, 25, 5);
    }

    [Test]
    public void GetActive_caps_results_at_max_records()
    {
        var conns = new List<PeerConnection>();
        for (var i = 0; i < 20; i++)
        {
            conns.Add(CreateMockPeerConnection($"10.0.0.{i}", 6881 + i, "aabbcc", $"peer_{i}", false));
        }

        _connectionManager.GetAllConnections().Returns(conns);

        var result = _controller.GetActive(maxRecords: 5);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var resources = (List<PeerConnectionLogResource>)okResult.Value;

        Assert.That(resources, Has.Count.EqualTo(5));
    }

    [Test]
    public void GetGraph_passes_max_records_limit_to_log_service()
    {
        var now = DateTime.UtcNow;
        var start = now.AddHours(-1);
        var end = now;

        _logService.GetByTimeRange(start, end, 200, 0).Returns(new List<PeerConnectionLog>());
        _torrentService.GetAll().Returns(new List<Torrent>());
        _peerDatabase.GetAllInfoHashes().Returns(new List<string>());

        var result = _controller.GetGraph(start, end, maxRecords: 200);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _logService.Received(1).GetByTimeRange(start, end, 200, 0);
    }
}
