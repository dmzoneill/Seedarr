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
}
