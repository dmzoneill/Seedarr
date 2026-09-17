using System;
using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Metrics;

namespace NzbDrone.Core.Test.Trackers.Metrics;

[TestFixture]
public class TrackerMetricServiceBatchingTest
{
    private ITrackerMetricRepository _metricRepository;
    private ITrackerMetricSnapshotRepository _snapshotRepository;
    private ITrackerEntryRepository _trackerEntryRepository;
    private ITorrentRepository _torrentRepository;
    private IEventAggregator _eventAggregator;
    private TrackerMetricService _subject;

    [SetUp]
    public void SetUp()
    {
        _metricRepository = Substitute.For<ITrackerMetricRepository>();
        _snapshotRepository = Substitute.For<ITrackerMetricSnapshotRepository>();
        _trackerEntryRepository = Substitute.For<ITrackerEntryRepository>();
        _torrentRepository = Substitute.For<ITorrentRepository>();
        _eventAggregator = Substitute.For<IEventAggregator>();

        _trackerEntryRepository.All().Returns(new List<TrackerEntry>());
        _torrentRepository.All().Returns(new List<Torrent>());

        _subject = new TrackerMetricService(
            _metricRepository,
            _snapshotRepository,
            _trackerEntryRepository,
            _torrentRepository,
            _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _subject?.Dispose();
    }

    [Test]
    public void Channel_batching_writes_snapshots_via_InsertMany_on_Flush()
    {
        var trackerUrl = "http://tracker.example/announce";
        var metric = new TrackerMetric { Id = 1, TrackerUrl = trackerUrl };
        _metricRepository.FindByUrl(trackerUrl).Returns(metric);

        for (var i = 0; i < 5; i++)
        {
            _subject.RecordAnnounce(trackerUrl, 1, 100, 50, 500, 120, true, 10, 5, 2);
        }

        _subject.RecordScrape(trackerUrl, 80, true, 10, 5, 100);

        _subject.Flush();

        _snapshotRepository.Received().InsertMany(Arg.Is<IList<TrackerMetricSnapshot>>(list => list.Count == 6));
    }

    [Test]
    public void PruneSnapshots_calls_PruneOlderThan_on_repository()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);

        _subject.PruneSnapshots(cutoff);

        _snapshotRepository.Received(1).PruneOlderThan(cutoff);
    }

    [Test]
    public void GetSummary_produces_correct_aggregated_hourly_metrics_from_database()
    {
        var now = DateTime.UtcNow;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var targetHour = currentHour.AddHours(-3);
        var bucketKey = targetHour.ToString("yyyy-MM-dd HH:00:00");

        var aggregated = new List<HourlyTrackerMetricPoint>
        {
            new()
            {
                Bucket = bucketKey,
                Uploaded = 12345,
                Downloaded = 6789,
                Announces = 4,
                PeersDiscovered = 15,
                AvgLatencyMs = 95.5
            }
        };

        _snapshotRepository.GetHourlyAggregatedMetrics(Arg.Any<DateTime>()).Returns(aggregated);
        _snapshotRepository.GetRecentSnapshots(Arg.Any<DateTime>()).Returns(new List<TrackerMetricSnapshot>());
        _metricRepository.All().Returns(new List<TrackerMetric>());

        var summary = _subject.GetSummary();

        Assert.That(summary.HourlyHistory, Is.Not.Null);
        Assert.That(summary.HourlyHistory.Count, Is.EqualTo(24));

        var targetPoint = summary.HourlyHistory.Find(h => h.Timestamp == targetHour);
        Assert.That(targetPoint, Is.Not.Null);
        Assert.That(targetPoint.Uploaded, Is.EqualTo(12345));
        Assert.That(targetPoint.Downloaded, Is.EqualTo(6789));
        Assert.That(targetPoint.Announces, Is.EqualTo(4));
        Assert.That(targetPoint.PeersDiscovered, Is.EqualTo(15));
        Assert.That(targetPoint.AvgLatencyMs, Is.EqualTo(95.5));
    }
}
