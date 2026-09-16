using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.TrackerBoost;

namespace NzbDrone.Core.Test.TrackerBoost;

[TestFixture]
public class TrackerBoostServiceTest
{
    private ITrackerBoostTrackerRepository _trackerRepository;
    private ITorrentService _torrentService;
    private ITrackerEntryService _trackerEntryService;
    private IIndexerRepository _indexerRepository;
    private IDownloadClientFactory _downloadClientFactory;
    private IConfigService _configService;
    private TrackerBoostService _service;

    [SetUp]
    public void SetUp()
    {
        _trackerRepository = Substitute.For<ITrackerBoostTrackerRepository>();
        _torrentService = Substitute.For<ITorrentService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _indexerRepository = Substitute.For<IIndexerRepository>();
        _downloadClientFactory = Substitute.For<IDownloadClientFactory>();
        _configService = Substitute.For<IConfigService>();

        _service = new TrackerBoostService(
            _trackerRepository,
            _torrentService,
            _trackerEntryService,
            _indexerRepository,
            _downloadClientFactory,
            _configService);

        TrackerBoostService.ResetMetricsAndHistory();
    }

    [TearDown]
    public void TearDown()
    {
        TrackerBoostService.ResetMetricsAndHistory();
    }

    [Test]
    public void BoostHistory_AddOrUpdate_merges_injected_trackers_without_duplicates()
    {
        const string infoHash = "1234567890abcdef1234567890abcdef12345678";

        TrackerBoostService.RecordBoostHistory(infoHash, new[] { "udp://tracker1.org:1337/announce", "udp://tracker2.org:1337/announce" });
        TrackerBoostService.RecordBoostHistory(infoHash, new[] { "udp://tracker2.org:1337/announce", "udp://tracker3.org:1337/announce" });

        var hasHistory = TrackerBoostService.TryGetBoostHistory(infoHash, out var history);

        Assert.That(hasHistory, Is.True);
        Assert.That(history.InjectedTrackers.Count, Is.EqualTo(3));
        Assert.That(history.InjectedTrackers.Contains("udp://tracker1.org:1337/announce"), Is.True);
        Assert.That(history.InjectedTrackers.Contains("udp://tracker2.org:1337/announce"), Is.True);
        Assert.That(history.InjectedTrackers.Contains("udp://tracker3.org:1337/announce"), Is.True);
    }

    [Test]
    public async Task BoostHistory_concurrent_record_and_read_is_thread_safe()
    {
        const int workerCount = 16;
        const int iterationsPerWorker = 100;
        const string infoHash = "fedcba0987654321fedcba0987654321fedcba09";

        var tasks = Enumerable.Range(0, workerCount).Select(workerId => Task.Run(() =>
        {
            for (var i = 0; i < iterationsPerWorker; i++)
            {
                var trackerUrl = $"udp://tracker-{workerId}-{i}.org:1337/announce";
                TrackerBoostService.RecordBoostHistory(infoHash, new[] { trackerUrl });

                var exists = TrackerBoostService.TryGetBoostHistory(infoHash, out var current);
                Assert.That(exists, Is.True);
                Assert.That(current.InjectedTrackers, Is.Not.Null);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        var success = TrackerBoostService.TryGetBoostHistory(infoHash, out var finalHistory);
        Assert.That(success, Is.True);
        Assert.That(finalHistory.InjectedTrackers.Count, Is.EqualTo(workerCount * iterationsPerWorker));
    }

    [Test]
    public async Task AtomicMetricCounters_concurrent_increments_and_adds_are_exact()
    {
        const int workerCount = 20;
        const int iterations = 500;

        var tasks = Enumerable.Range(0, workerCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                TrackerBoostService.IncrementTorrentsBoosted();
                TrackerBoostService.AddTrackersInjected(3);
                TrackerBoostService.AddVerifiedMatches(5);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.That(TrackerBoostService.TotalTorrentsBoosted, Is.EqualTo(workerCount * iterations));
        Assert.That(TrackerBoostService.TotalTrackersInjected, Is.EqualTo(workerCount * iterations * 3));
        Assert.That(TrackerBoostService.TotalVerifiedMatchesCount, Is.EqualTo(workerCount * iterations * 5));
    }

    [Test]
    public void ResetMetricsAndHistory_clears_metrics_and_history()
    {
        const string infoHash = "aaaabbbbccccddddeeeeffff0000111122223333";
        TrackerBoostService.RecordBoostHistory(infoHash, new[] { "udp://tracker.org:1337/announce" });
        TrackerBoostService.IncrementTorrentsBoosted();
        TrackerBoostService.AddTrackersInjected(10);
        TrackerBoostService.AddVerifiedMatches(15);

        TrackerBoostService.ResetMetricsAndHistory();

        Assert.That(TrackerBoostService.TryGetBoostHistory(infoHash, out _), Is.False);
        Assert.That(TrackerBoostService.TotalTorrentsBoosted, Is.EqualTo(0));
        Assert.That(TrackerBoostService.TotalTrackersInjected, Is.EqualTo(0));
        Assert.That(TrackerBoostService.TotalVerifiedMatchesCount, Is.EqualTo(0));
    }

    [Test]
    public async Task ProbeTrackerHealthAsync_batches_repository_updates_to_prevent_write_lock_contention()
    {
        var trackers = new List<TrackerBoostTracker>
        {
            new() { Id = 1, Url = "udp://tracker1.invalid:1337/announce", Host = "tracker1.invalid", Port = 1337, Protocol = TrackerProtocol.Udp, Enabled = true },
            new() { Id = 2, Url = "udp://tracker2.invalid:1337/announce", Host = "tracker2.invalid", Port = 1337, Protocol = TrackerProtocol.Udp, Enabled = true },
            new() { Id = 3, Url = "udp://tracker3.invalid:1337/announce", Host = "tracker3.invalid", Port = 1337, Protocol = TrackerProtocol.Udp, Enabled = true },
            new() { Id = 4, Url = "udp://tracker4.invalid:1337/announce", Host = "tracker4.invalid", Port = 1337, Protocol = TrackerProtocol.Udp, Enabled = true },
            new() { Id = 5, Url = "udp://tracker5.invalid:1337/announce", Host = "tracker5.invalid", Port = 1337, Protocol = TrackerProtocol.Udp, Enabled = true }
        };

        _trackerRepository.All().Returns(trackers);

        var tested = await _service.ProbeTrackerHealthAsync();

        Assert.That(tested, Is.EqualTo(5));
        _trackerRepository.DidNotReceive().Update(Arg.Any<TrackerBoostTracker>());
        _trackerRepository.Received(1).UpdateMany(Arg.Is<IEnumerable<TrackerBoostTracker>>(list => list.Count() == 5));
    }

    [Test]
    public void UdpReceive_with_cancelled_token_throws_OperationCanceledException_handled_gracefully()
    {
        using var client = new UdpClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await client.ReceiveAsync(cts.Token);
        });
    }

    [Test]
    public async Task HarvestFromActiveDownloadsAsync_skips_trackers_from_private_torrents_and_items()
    {
        var privateTorrent = new Torrent { Id = 1, IsPrivate = true, InfoHash = "privatehash1", Name = "Private" };
        var publicTorrent = new Torrent { Id = 2, IsPrivate = false, InfoHash = "publichash2", Name = "Public" };
        _torrentService.GetAll().Returns(new List<Torrent> { privateTorrent, publicTorrent });

        var privateTrackerEntry = new TrackerEntry { Id = 1, TorrentId = 1, Url = "udp://tracker.private.org:1337/announce" };
        var publicTrackerEntry = new TrackerEntry { Id = 2, TorrentId = 2, Url = "udp://tracker.public.org:1337/announce" };
        _trackerEntryService.All().Returns(new List<TrackerEntry> { privateTrackerEntry, publicTrackerEntry });

        var clientDef = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", Enable = true, ClientType = "QBitTorrent" };
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { clientDef });

        var downloadClient = Substitute.For<IDownloadClient>();
        var privateItem = new DownloadClientItem { InfoHash = "clientprivhash", IsPrivate = true };
        var publicItem = new DownloadClientItem { InfoHash = "publichash2", IsPrivate = false };
        downloadClient.GetItems().Returns(new List<DownloadClientItem> { privateItem, publicItem });
        downloadClient.GetTrackers("clientprivhash").Returns(new List<string> { "udp://client.private.org:1337/announce" });
        downloadClient.GetTrackers("publichash2").Returns(new List<string> { "udp://client.public.org:1337/announce" });

        _downloadClientFactory.CreateClient(clientDef).Returns(downloadClient);

        _trackerRepository.FindByUrl(Arg.Any<string>()).Returns((TrackerBoostTracker)null);
        _trackerRepository.Insert(Arg.Any<TrackerBoostTracker>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<TrackerBoostTracker>();
            t.Id = 10;
            return t;
        });

        var count = await _service.HarvestFromActiveDownloadsAsync();

        Assert.That(count, Is.EqualTo(2));
        _trackerRepository.Received(1).Insert(Arg.Is<TrackerBoostTracker>(t => t.Url == "udp://tracker.public.org:1337/announce"));
        _trackerRepository.Received(1).Insert(Arg.Is<TrackerBoostTracker>(t => t.Url == "udp://client.public.org:1337/announce"));
        _trackerRepository.DidNotReceive().Insert(Arg.Is<TrackerBoostTracker>(t => t.Url == "udp://tracker.private.org:1337/announce"));
        _trackerRepository.DidNotReceive().Insert(Arg.Is<TrackerBoostTracker>(t => t.Url == "udp://client.private.org:1337/announce"));
    }

    [TestCase("https://tracker.private.org/44817e2f66221a38b0029e8e098b9aff/announce", false)]
    [TestCase("https://tracker.private.org/announce/44817e2f66221a38b0029e8e098b9aff", false)]
    [TestCase("https://tracker.private.org/0123456789abcdef0123456789abcdef", false)]
    [TestCase("https://tracker.public.org/announce", true)]
    [TestCase("udp://tracker.public.org:1337/announce", true)]
    public void IsValidPublicTrackerUrl_rejects_path_based_passkey_tokens(string url, bool expected)
    {
        var result = TrackerBoostService.IsValidPublicTrackerUrl(url);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task BoostHashAsync_skips_private_torrent_in_seedarr_when_force_false()
    {
        var privateTorrent = new Torrent { Id = 1, IsPrivate = true, InfoHash = "privhash1", Name = "Private Torrent" };
        _torrentService.GetAll().Returns(new List<Torrent> { privateTorrent });

        var result = await _service.BoostHashAsync("privhash1", force: false);

        Assert.That(result.IsPrivate, Is.True);
        Assert.That(result.Boosted, Is.False);
        Assert.That(result.Message, Does.Contain("Private torrents are protected"));
    }

    [Test]
    public async Task BoostHashAsync_skips_private_torrent_in_download_client_when_force_false()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());
        var clientDef = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", Enable = true, ClientType = "QBitTorrent" };
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { clientDef });

        var downloadClient = Substitute.For<IDownloadClient>();
        downloadClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { InfoHash = "clientprivhash", IsPrivate = true, Title = "Client Private" }
        });
        _downloadClientFactory.CreateClient(clientDef).Returns(downloadClient);

        var result = await _service.BoostHashAsync("clientprivhash", force: false);

        Assert.That(result.IsPrivate, Is.True);
        Assert.That(result.Boosted, Is.False);
        Assert.That(result.Message, Does.Contain("Private torrents are protected"));
        downloadClient.DidNotReceive().AddTrackers(Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public async Task InjectTrackerToHashAsync_skips_private_torrent_in_download_client_when_force_false()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());
        var clientDef = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", Enable = true, ClientType = "QBitTorrent" };
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { clientDef });

        var downloadClient = Substitute.For<IDownloadClient>();
        downloadClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { InfoHash = "clientprivhash", IsPrivate = true }
        });
        _downloadClientFactory.CreateClient(clientDef).Returns(downloadClient);

        var result = await _service.InjectTrackerToHashAsync("clientprivhash", "udp://tracker.public.org:1337/announce", force: false);

        Assert.That(result.IsPrivate, Is.True);
        Assert.That(result.Boosted, Is.False);
        Assert.That(result.Message, Does.Contain("Private torrents are protected"));
        downloadClient.DidNotReceive().AddTrackers(Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public async Task InjectTrackerToHashAsync_allows_private_torrent_when_force_true()
    {
        _torrentService.GetAll().Returns(new List<Torrent>());
        var clientDef = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", Enable = true, ClientType = "QBitTorrent" };
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { clientDef });

        var downloadClient = Substitute.For<IDownloadClient>();
        downloadClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { InfoHash = "clientprivhash", IsPrivate = true }
        });
        downloadClient.AddTrackers("clientprivhash", Arg.Any<IEnumerable<string>>()).Returns(true);
        _downloadClientFactory.CreateClient(clientDef).Returns(downloadClient);

        var result = await _service.InjectTrackerToHashAsync("clientprivhash", "udp://tracker.public.org:1337/announce", force: true);

        Assert.That(result.Boosted, Is.True);
        downloadClient.Received(1).AddTrackers("clientprivhash", Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public async Task BoostAllTorrentsAsync_skips_private_torrents_in_seedarr_and_download_clients()
    {
        var privateTorrent = new Torrent { Id = 1, IsPrivate = true, InfoHash = "seedarrprivhash", Name = "Seedarr Private" };
        _torrentService.GetAll().Returns(new List<Torrent> { privateTorrent });

        var clientDef = new DownloadClientDefinition { Id = 1, Name = "qBittorrent", Enable = true, ClientType = "QBitTorrent" };
        _downloadClientFactory.All().Returns(new List<DownloadClientDefinition> { clientDef });

        var downloadClient = Substitute.For<IDownloadClient>();
        downloadClient.GetItems().Returns(new List<DownloadClientItem>
        {
            new() { InfoHash = "seedarrprivhash", IsPrivate = true },
            new() { InfoHash = "clientprivhash", IsPrivate = true }
        });
        _downloadClientFactory.CreateClient(clientDef).Returns(downloadClient);

        var results = await _service.BoostAllTorrentsAsync();

        Assert.That(results, Is.Empty);
        downloadClient.DidNotReceive().AddTrackers(Arg.Any<string>(), Arg.Any<IEnumerable<string>>());
    }
}
