using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;
using NzbDrone.Core.Trackers.Metrics;

namespace NzbDrone.Core.Test.Trackers;

[TestFixture]
public class TrackerMetricServiceTests
{
    private ITrackerMetricRepository _metricRepository;
    private ITrackerMetricSnapshotRepository _snapshotRepository;
    private ITrackerEntryRepository _trackerEntryRepository;
    private ITorrentRepository _torrentRepository;
    private IEventAggregator _eventAggregator;
    private TrackerMetricService _service;

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

        _service = new TrackerMetricService(
            _metricRepository,
            _snapshotRepository,
            _trackerEntryRepository,
            _torrentRepository,
            _eventAggregator);
    }

    [Test]
    public void Failed_announce_does_not_distort_Min_Max_Avg_response_times()
    {
        var trackerUrl = "http://tracker.example.com:80/announce";
        var metric = new TrackerMetric
        {
            Id = 1,
            TrackerUrl = trackerUrl,
            MinResponseTimeMs = 100,
            MaxResponseTimeMs = 200,
            AvgResponseTimeMs = 150.0,
            LastResponseTimeMs = 150,
            SuccessfulAnnounces = 5,
            TotalAnnounces = 5
        };

        _metricRepository.FindByUrl(trackerUrl).Returns(metric);

        // Record a failed announce with 15000ms timeout
        var result = _service.RecordAnnounce(
            trackerUrl: trackerUrl,
            torrentId: 1,
            uploaded: 0,
            downloaded: 0,
            left: 1000,
            responseTimeMs: 15000,
            success: false,
            seeders: 0,
            leechers: 0,
            peersCount: 0,
            error: "Socket timed out");

        Assert.That(result.MinResponseTimeMs, Is.EqualTo(100));
        Assert.That(result.MaxResponseTimeMs, Is.EqualTo(200));
        Assert.That(result.AvgResponseTimeMs, Is.EqualTo(150.0));
        Assert.That(result.LastResponseTimeMs, Is.EqualTo(150));
        Assert.That(result.FailedAnnounces, Is.EqualTo(1));
        Assert.That(result.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(result.LastErrorMessage, Is.EqualTo("Socket timed out"));

        // Record another failure with 0ms immediate abort
        var result2 = _service.RecordAnnounce(
            trackerUrl: trackerUrl,
            torrentId: 1,
            uploaded: 0,
            downloaded: 0,
            left: 1000,
            responseTimeMs: 0,
            success: false,
            seeders: 0,
            leechers: 0,
            peersCount: 0,
            error: "Connection aborted");

        Assert.That(result2.MinResponseTimeMs, Is.EqualTo(100));
        Assert.That(result2.MaxResponseTimeMs, Is.EqualTo(200));
        Assert.That(result2.AvgResponseTimeMs, Is.EqualTo(150.0));
        Assert.That(result2.LastResponseTimeMs, Is.EqualTo(150));
        Assert.That(result2.ConsecutiveFailures, Is.EqualTo(2));
        Assert.That(result2.Status, Is.EqualTo("Degraded"));
    }

    [Test]
    public void Successful_announce_and_scrape_update_response_time_metrics()
    {
        var trackerUrl = "http://tracker.example.com:80/announce";
        var metric = new TrackerMetric
        {
            Id = 1,
            TrackerUrl = trackerUrl
        };

        _metricRepository.FindByUrl(trackerUrl).Returns(metric);

        // 1. First successful announce: 120ms
        _service.RecordAnnounce(
            trackerUrl: trackerUrl,
            torrentId: 1,
            uploaded: 1000,
            downloaded: 500,
            left: 500,
            responseTimeMs: 120,
            success: true,
            seeders: 10,
            leechers: 5,
            peersCount: 15);

        Assert.That(metric.MinResponseTimeMs, Is.EqualTo(120));
        Assert.That(metric.MaxResponseTimeMs, Is.EqualTo(120));
        Assert.That(metric.AvgResponseTimeMs, Is.EqualTo(120.0));
        Assert.That(metric.LastResponseTimeMs, Is.EqualTo(120));
        Assert.That(metric.SuccessfulAnnounces, Is.EqualTo(1));

        // 2. Second successful announce: 80ms
        _service.RecordAnnounce(
            trackerUrl: trackerUrl,
            torrentId: 1,
            uploaded: 1000,
            downloaded: 500,
            left: 500,
            responseTimeMs: 80,
            success: true,
            seeders: 10,
            leechers: 5,
            peersCount: 15);

        Assert.That(metric.MinResponseTimeMs, Is.EqualTo(80));
        Assert.That(metric.MaxResponseTimeMs, Is.EqualTo(120));
        Assert.That(metric.LastResponseTimeMs, Is.EqualTo(80));
        // EMA: 120 * 0.85 + 80 * 0.15 = 102 + 12 = 114.0
        Assert.That(metric.AvgResponseTimeMs, Is.EqualTo(114.0));
        Assert.That(metric.SuccessfulAnnounces, Is.EqualTo(2));

        // 3. Successful scrape: 50ms
        _service.RecordScrape(
            trackerUrl: trackerUrl,
            responseTimeMs: 50,
            success: true,
            seeders: 12,
            leechers: 4,
            completed: 100);

        Assert.That(metric.MinResponseTimeMs, Is.EqualTo(50));
        Assert.That(metric.MaxResponseTimeMs, Is.EqualTo(120));
        Assert.That(metric.LastResponseTimeMs, Is.EqualTo(50));
        // EMA: 114.0 * 0.85 + 50 * 0.15 = 96.9 + 7.5 = 104.4
        Assert.That(metric.AvgResponseTimeMs, Is.EqualTo(104.4));
        Assert.That(metric.SuccessfulScrapes, Is.EqualTo(1));
    }

    [Test]
    public void Hourly_buckets_cover_snapshots_without_gap_loss()
    {
        var now = DateTime.UtcNow;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var oldestBucketStart = currentHour.AddHours(-24);

        var snapshots = new List<TrackerMetricSnapshot>
        {
            // Snapshot in the oldest hour (24 hours ago + 15 min): previously lost due to 59-minute gap loss
            new()
            {
                Id = 1,
                TrackerMetricId = 1,
                TrackerUrl = "http://tracker.test/announce",
                Timestamp = oldestBucketStart.AddMinutes(15),
                ResponseTimeMs = 50,
                Uploaded = 1000,
                Downloaded = 500,
                IsSuccess = true,
                Operation = "Announce"
            },
            // Snapshot in middle hour (12 hours ago + 30 min)
            new()
            {
                Id = 2,
                TrackerMetricId = 1,
                TrackerUrl = "http://tracker.test/announce",
                Timestamp = currentHour.AddHours(-12).AddMinutes(30),
                ResponseTimeMs = 60,
                Uploaded = 2000,
                Downloaded = 1000,
                IsSuccess = true,
                Operation = "Announce"
            },
            // Snapshot in newest bucket (1 hour ago + 45 min)
            new()
            {
                Id = 3,
                TrackerMetricId = 1,
                TrackerUrl = "http://tracker.test/announce",
                Timestamp = currentHour.AddHours(-1).AddMinutes(45),
                ResponseTimeMs = 70,
                Uploaded = 3000,
                Downloaded = 1500,
                IsSuccess = true,
                Operation = "Announce"
            }
        };

        _snapshotRepository.GetRecentSnapshots(Arg.Any<DateTime>()).Returns(snapshots);
        _metricRepository.All().Returns(new List<TrackerMetric>());

        var summary = _service.GetSummary();

        Assert.That(summary.HourlyHistory, Is.Not.Null);
        Assert.That(summary.HourlyHistory.Count, Is.EqualTo(24));

        // Oldest bucket must anchor to exactly 24 hours prior to current hour
        Assert.That(summary.HourlyHistory[0].Timestamp, Is.EqualTo(oldestBucketStart));
        Assert.That(summary.HourlyHistory[0].Announces, Is.EqualTo(1));
        Assert.That(summary.HourlyHistory[0].Uploaded, Is.EqualTo(1000));

        // Middle bucket at index 12 (12 hours after oldestBucketStart)
        Assert.That(summary.HourlyHistory[12].Timestamp, Is.EqualTo(oldestBucketStart.AddHours(12)));
        Assert.That(summary.HourlyHistory[12].Announces, Is.EqualTo(1));
        Assert.That(summary.HourlyHistory[12].Uploaded, Is.EqualTo(2000));

        // Newest bucket at index 23
        Assert.That(summary.HourlyHistory[23].Timestamp, Is.EqualTo(currentHour.AddHours(-1)));
        Assert.That(summary.HourlyHistory[23].Announces, Is.EqualTo(1));
        Assert.That(summary.HourlyHistory[23].Uploaded, Is.EqualTo(3000));

        // Total announces across all 24 buckets must equal total snapshots without gap loss
        Assert.That(summary.HourlyHistory.Sum(h => h.Announces), Is.EqualTo(3));
        Assert.That(summary.HourlyHistory.Sum(h => h.Uploaded), Is.EqualTo(6000));
    }

    [Test]
    public void Percentile_calculations_are_correct_and_prevent_division_by_zero()
    {
        // Empty sequence
        var empty = new List<double>();
        Assert.That(TrackerMetricService.CalculatePercentile(empty, 50), Is.EqualTo(0.0));
        Assert.That(TrackerMetricService.CalculatePercentile(empty, 95), Is.EqualTo(0.0));
        Assert.That(TrackerMetricService.CalculatePercentile(empty, 99), Is.EqualTo(0.0));

        // Null sequence
        Assert.That(TrackerMetricService.CalculatePercentile((List<double>)null, 50), Is.EqualTo(0.0));

        // Single item
        var single = new List<double> { 100.0 };
        Assert.That(TrackerMetricService.CalculatePercentile(single, 50), Is.EqualTo(100.0));
        Assert.That(TrackerMetricService.CalculatePercentile(single, 95), Is.EqualTo(100.0));
        Assert.That(TrackerMetricService.CalculatePercentile(single, 99), Is.EqualTo(100.0));

        // 5 items: 10, 20, 30, 40, 50
        var five = new List<double> { 50.0, 10.0, 40.0, 20.0, 30.0 };
        Assert.That(TrackerMetricService.CalculatePercentile(five, 50), Is.EqualTo(30.0));
        Assert.That(TrackerMetricService.CalculatePercentile(five, 95), Is.EqualTo(48.0));
        Assert.That(TrackerMetricService.CalculatePercentile(five, 99), Is.EqualTo(49.6));

        // 100 values: 1..100
        var hundred = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        Assert.That(TrackerMetricService.CalculatePercentile(hundred, 50), Is.EqualTo(50.5));
        Assert.That(TrackerMetricService.CalculatePercentile(hundred, 95), Is.EqualTo(95.05));
        Assert.That(TrackerMetricService.CalculatePercentile(hundred, 99), Is.EqualTo(99.01));

        // Boundary checks
        Assert.That(TrackerMetricService.CalculatePercentile(hundred, 0), Is.EqualTo(1.0));
        Assert.That(TrackerMetricService.CalculatePercentile(hundred, 100), Is.EqualTo(100.0));
    }

    [Test]
    public void GetSummary_calculates_SLA_percentiles_ignoring_failures()
    {
        var snapshots = new List<TrackerMetricSnapshot>
        {
            new() { Id = 1, ResponseTimeMs = 50, IsSuccess = true, Operation = "Announce" },
            new() { Id = 2, ResponseTimeMs = 100, IsSuccess = true, Operation = "Announce" },
            new() { Id = 3, ResponseTimeMs = 150, IsSuccess = true, Operation = "Announce" },
            new() { Id = 4, ResponseTimeMs = 200, IsSuccess = true, Operation = "Announce" },
            new() { Id = 5, ResponseTimeMs = 250, IsSuccess = true, Operation = "Announce" },
            // Failed announce timeout: must be excluded from SLA percentiles
            new() { Id = 6, ResponseTimeMs = 15000, IsSuccess = false, Operation = "Announce" }
        };

        _snapshotRepository.GetRecentSnapshots(Arg.Any<DateTime>()).Returns(snapshots);
        _metricRepository.All().Returns(new List<TrackerMetric>());

        var summary = _service.GetSummary();

        // 5 successful items: 50, 100, 150, 200, 250
        Assert.That(summary.LatencyP50Ms, Is.EqualTo(150.0));
        Assert.That(summary.P50LatencyMs, Is.EqualTo(150.0));
        Assert.That(summary.LatencyP95Ms, Is.EqualTo(240.0));
        Assert.That(summary.P95LatencyMs, Is.EqualTo(240.0));
        Assert.That(summary.LatencyP99Ms, Is.EqualTo(248.0));
        Assert.That(summary.P99LatencyMs, Is.EqualTo(248.0));
    }

    [Test]
    public void GetMetric_populates_SLA_percentiles_from_history()
    {
        var metric = new TrackerMetric
        {
            Id = 42,
            TrackerUrl = "http://tracker.test/announce",
            AvgResponseTimeMs = 80.0
        };

        var history = new List<TrackerMetricSnapshot>
        {
            new() { Id = 1, TrackerMetricId = 42, ResponseTimeMs = 60, IsSuccess = true },
            new() { Id = 2, TrackerMetricId = 42, ResponseTimeMs = 80, IsSuccess = true },
            new() { Id = 3, TrackerMetricId = 42, ResponseTimeMs = 100, IsSuccess = true }
        };

        _metricRepository.Get(42).Returns(metric);
        _snapshotRepository.GetHistory(42, Arg.Any<DateTime>()).Returns(history);

        var result = _service.GetMetric(42);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.LatencyP50Ms, Is.EqualTo(80.0));
        Assert.That(result.LatencyP95Ms, Is.EqualTo(98.0));
        Assert.That(result.LatencyP99Ms, Is.EqualTo(99.6));
    }
}
