using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerServer;
using Seedarr.Api.V1.TrackerServer;

namespace NzbDrone.Core.Test.TrackerServer;

[TestFixture]
public class TrackerServerControllerTests
{
    private IPeerDatabase _peerDatabase;
    private ITorrentService _torrentService;
    private IConfigService _configService;
    private IDownloadHistoryRepository _downloadHistoryRepository;
    private TrackerServerController _controller;

    [SetUp]
    public void SetUp()
    {
        _peerDatabase = Substitute.For<IPeerDatabase>();
        _torrentService = Substitute.For<ITorrentService>();
        _configService = Substitute.For<IConfigService>();
        _downloadHistoryRepository = Substitute.For<IDownloadHistoryRepository>();

        _controller = new TrackerServerController(
            _peerDatabase,
            _torrentService,
            _configService,
            _downloadHistoryRepository);
    }

    [Test]
    public void GetTrackedTorrents_returns_empty_and_does_not_query_database_when_no_hashes()
    {
        _peerDatabase.GetAllInfoHashes().Returns(new List<string>());

        var result = _controller.GetTrackedTorrents();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        var list = okResult.Value as IEnumerable<object>;
        Assert.That(list, Is.Empty);

        _torrentService.DidNotReceive().GetAll();
        _torrentService.DidNotReceive().GetByInfoHashes(Arg.Any<IEnumerable<string>>());
        _downloadHistoryRepository.DidNotReceive().All();
        _downloadHistoryRepository.DidNotReceive().GetLatestByInfoHashes(Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public void GetTrackedTorrents_queries_targeted_torrents_and_history_when_hashes_exist()
    {
        var hashes = new List<string> { "hash1", "hash2" };
        _peerDatabase.GetAllInfoHashes().Returns(hashes);
        _peerDatabase.GetStats(Arg.Any<string>()).Returns(new ScrapeStats());
        _peerDatabase.GetPeers(Arg.Any<string>()).Returns(new List<TrackerPeerEntry>());

        var torrents = new List<Torrent>
        {
            new() { InfoHash = "hash1", Name = "Torrent 1", Uploaded = 1000 }
        };
        _torrentService.GetByInfoHashes(Arg.Any<IEnumerable<string>>()).Returns(torrents);

        var history = new Dictionary<string, DownloadHistory>(StringComparer.OrdinalIgnoreCase)
        {
            { "hash1", new DownloadHistory { InfoHash = "hash1", Source = "Radarr" } }
        };
        _downloadHistoryRepository.GetLatestByInfoHashes(Arg.Any<IEnumerable<string>>()).Returns(history);

        var result = _controller.GetTrackedTorrents();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        var list = (okResult.Value as IEnumerable<object>)?.ToList();
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Count, Is.EqualTo(2));

        // Ensure full table scans were avoided
        _torrentService.DidNotReceive().GetAll();
        _downloadHistoryRepository.DidNotReceive().All();

        // Ensure targeted lookups were performed
        _torrentService.Received(1).GetByInfoHashes(Arg.Is<IEnumerable<string>>(h => h.SequenceEqual(hashes)));
        _downloadHistoryRepository.Received(1).GetLatestByInfoHashes(Arg.Is<IEnumerable<string>>(h => h.SequenceEqual(hashes)));
    }

    [Test]
    public void GetStats_uses_targeted_torrents_query_when_hashes_exist()
    {
        var hashes = new List<string> { "hash1", "hash2" };
        _peerDatabase.GetAllInfoHashes().Returns(hashes);
        _peerDatabase.GetTotalTorrentCount().Returns(2);
        _peerDatabase.GetTotalPeerCount().Returns(10);

        var matchingTorrents = new List<Torrent>
        {
            new() { InfoHash = "hash1" }
        };
        _torrentService.GetByInfoHashes(Arg.Any<IEnumerable<string>>()).Returns(matchingTorrents);

        var result = _controller.GetStats();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _torrentService.DidNotReceive().GetAll();
        _torrentService.Received(1).GetByInfoHashes(Arg.Is<IEnumerable<string>>(h => h.SequenceEqual(hashes)));
    }

    [Test]
    public void GetStats_does_not_query_torrents_when_no_hashes_exist()
    {
        _peerDatabase.GetAllInfoHashes().Returns(new List<string>());
        _peerDatabase.GetTotalTorrentCount().Returns(0);
        _peerDatabase.GetTotalPeerCount().Returns(0);

        var result = _controller.GetStats();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _torrentService.DidNotReceive().GetAll();
        _torrentService.DidNotReceive().GetByInfoHashes(Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public void GetPeersForTorrent_returns_peers_from_database()
    {
        var peers = new List<TrackerPeerEntry>
        {
            new() { Ip = "127.0.0.1", Port = 6881, PeerId = "peer1", LastAnnounce = DateTime.UtcNow }
        };
        _peerDatabase.GetPeers("hash1").Returns(peers);

        var result = _controller.GetPeersForTorrent("hash1");

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
    }
}
