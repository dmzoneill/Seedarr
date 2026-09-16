using System;
using System.Collections.Generic;
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
}
