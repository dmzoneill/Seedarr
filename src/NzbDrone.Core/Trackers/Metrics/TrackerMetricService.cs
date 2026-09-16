using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Trackers.Metrics;

public interface ITrackerMetricService
{
    TrackerMetric RecordAnnounce(
        string trackerUrl,
        int torrentId,
        long uploaded,
        long downloaded,
        long left,
        long responseTimeMs,
        bool success,
        int seeders,
        int leechers,
        int peersCount,
        string error = null);

    TrackerMetric RecordScrape(
        string trackerUrl,
        long responseTimeMs,
        bool success,
        int seeders,
        int leechers,
        int completed,
        string error = null);

    List<TrackerMetric> GetAllMetrics();
    TrackerMetric GetMetric(int id);
    TrackerMetric GetMetricByUrl(string url);
    TrackerMetricsSummary GetSummary();
    List<TrackerMetricSnapshot> GetHistory(int id, int hours = 24);
    void ResetMetrics(int id);
    void DeleteMetric(int id);
    void SeedFromExistingTrackers();
}

public class TrackerMetricsSummary
{
    public int TotalTrackers { get; set; }
    public int HealthyTrackers { get; set; }
    public int DegradedTrackers { get; set; }
    public int OfflineTrackers { get; set; }
    public long TotalUploaded { get; set; }
    public long TotalDownloaded { get; set; }
    public double GlobalRatio => TotalDownloaded > 0 ? Math.Round((double)TotalUploaded / TotalDownloaded, 3) : (TotalUploaded > 0 ? 999.0 : 0.0);
    public long TotalAnnounces { get; set; }
    public long SuccessfulAnnounces { get; set; }
    public long FailedAnnounces { get; set; }
    public double AnnounceSuccessRate => TotalAnnounces > 0 ? Math.Round((double)SuccessfulAnnounces / TotalAnnounces * 100.0, 1) : 100.0;
    public long TotalScrapes { get; set; }
    public long SuccessfulScrapes { get; set; }
    public long TotalPeersDiscovered { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public double LatencyP50Ms { get; set; }
    public double LatencyP95Ms { get; set; }
    public double LatencyP99Ms { get; set; }
    public double P50LatencyMs { get => LatencyP50Ms; set => LatencyP50Ms = value; }
    public double P95LatencyMs { get => LatencyP95Ms; set => LatencyP95Ms = value; }
    public double P99LatencyMs { get => LatencyP99Ms; set => LatencyP99Ms = value; }
    public double P50ResponseTimeMs { get => LatencyP50Ms; set => LatencyP50Ms = value; }
    public double P95ResponseTimeMs { get => LatencyP95Ms; set => LatencyP95Ms = value; }
    public double P99ResponseTimeMs { get => LatencyP99Ms; set => LatencyP99Ms = value; }
    public Dictionary<string, int> ProtocolDistribution { get; set; } = new();
    public Dictionary<string, int> HealthDistribution { get; set; } = new();
    public List<TrackerMetricItemSummary> TopUploadTrackers { get; set; } = new();
    public List<TrackerMetricItemSummary> TopPeerTrackers { get; set; } = new();
    public List<HourlyTrafficPoint> HourlyHistory { get; set; } = new();
}

public class TrackerMetricItemSummary
{
    public int Id { get; set; }
    public string TrackerUrl { get; set; }
    public string Domain { get; set; }
    public string Protocol { get; set; }
    public string Status { get; set; }
    public long TotalUploaded { get; set; }
    public long TotalDownloaded { get; set; }
    public long TotalPeersDiscovered { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public double SuccessRate { get; set; }
}

public class HourlyTrafficPoint
{
    public string TimeLabel { get; set; }
    public DateTime Timestamp { get; set; }
    public long Uploaded { get; set; }
    public long Downloaded { get; set; }
    public int Announces { get; set; }
    public int PeersDiscovered { get; set; }
    public double AvgLatencyMs { get; set; }
}

public class TrackerMetricService : ITrackerMetricService
{
    private readonly ITrackerMetricRepository _metricRepository;
    private readonly ITrackerMetricSnapshotRepository _snapshotRepository;
    private readonly ITrackerEntryRepository _trackerEntryRepository;
    private readonly ITorrentRepository _torrentRepository;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly object _lock = new();

    public TrackerMetricService(
        ITrackerMetricRepository metricRepository,
        ITrackerMetricSnapshotRepository snapshotRepository,
        ITrackerEntryRepository trackerEntryRepository,
        ITorrentRepository torrentRepository,
        IEventAggregator eventAggregator = null)
    {
        _metricRepository = metricRepository;
        _snapshotRepository = snapshotRepository;
        _trackerEntryRepository = trackerEntryRepository;
        _torrentRepository = torrentRepository;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();

        Task.Run(SeedFromExistingTrackers);
    }

    public void SeedFromExistingTrackers()
    {
        try
        {
            var trackerEntries = _trackerEntryRepository.All().ToList();
            var torrents = _torrentRepository.All().ToList();

            foreach (var t in torrents)
            {
                if (!string.IsNullOrWhiteSpace(t.TrackerUrl))
                {
                    GetOrCreateMetric(t.TrackerUrl);
                }
            }

            foreach (var entry in trackerEntries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Url))
                {
                    var metric = GetOrCreateMetric(entry.Url);
                    if (metric != null && metric.TotalAnnounces == 0 && entry.TotalAnnounces > 0)
                    {
                        metric.TotalAnnounces = entry.TotalAnnounces;
                        metric.SuccessfulAnnounces = entry.SuccessfulAnnounces;
                        metric.FailedAnnounces = Math.Max(0, entry.TotalAnnounces - entry.SuccessfulAnnounces);
                        metric.LastAnnounce = entry.LastAnnounce;
                        metric.LastSeeders = entry.Seeders;
                        metric.LastLeechers = entry.Leechers;
                        if (entry.LastResponseTime > 0)
                        {
                            metric.LastResponseTimeMs = (long)entry.LastResponseTime;
                            metric.AvgResponseTimeMs = entry.LastResponseTime;
                        }

                        _metricRepository.Update(metric);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed initializing tracker metrics from existing records");
        }
    }

    public TrackerMetric RecordAnnounce(
        string trackerUrl,
        int torrentId,
        long uploaded,
        long downloaded,
        long left,
        long responseTimeMs,
        bool success,
        int seeders,
        int leechers,
        int peersCount,
        string error = null)
    {
        if (string.IsNullOrWhiteSpace(trackerUrl))
        {
            return null;
        }

        lock (_lock)
        {
            var metric = GetOrCreateMetric(trackerUrl);
            var now = DateTime.UtcNow;

            metric.TotalAnnounces++;
            metric.LastAnnounce = now;

            if (success)
            {
                UpdateLatencyMetrics(metric, responseTimeMs);
                metric.SuccessfulAnnounces++;
                metric.ConsecutiveFailures = 0;
                metric.LastSuccess = now;
                metric.LastErrorMessage = null;
                metric.Status = "Working";

                if (seeders > 0)
                {
                    metric.LastSeeders = seeders;
                }

                if (leechers > 0)
                {
                    metric.LastLeechers = leechers;
                }

                if (peersCount > 0)
                {
                    metric.LastPeers = peersCount;
                    metric.TotalPeersDiscovered += peersCount;
                }

                metric.TotalUploaded += uploaded;
                metric.TotalDownloaded += downloaded;
                metric.TotalLeft = left;
                metric.SessionUploaded += uploaded;
                metric.SessionDownloaded += downloaded;
            }
            else
            {
                metric.FailedAnnounces++;
                metric.ConsecutiveFailures++;
                metric.LastErrorTime = now;
                metric.LastErrorMessage = error ?? "Announce failed";

                if (metric.ConsecutiveFailures >= 5)
                {
                    metric.Status = "Offline";
                }
                else if (metric.ConsecutiveFailures >= 2)
                {
                    metric.Status = "Degraded";
                }

                _eventAggregator?.PublishEvent(new TrackerUnreachableEvent(null, trackerUrl, error ?? "Announce failed"));
            }

            _metricRepository.Update(metric);
            _eventAggregator?.PublishEvent(new TrackerMetricUpdatedEvent(metric.TrackerUrl, metric.TotalAnnounces, metric.TotalUploaded, metric.TotalDownloaded, metric.Status));

            // Record snapshot for time-series history
            try
            {
                _snapshotRepository.Insert(new TrackerMetricSnapshot
                {
                    TrackerMetricId = metric.Id,
                    TrackerUrl = metric.TrackerUrl,
                    Timestamp = now,
                    ResponseTimeMs = responseTimeMs,
                    Uploaded = uploaded,
                    Downloaded = downloaded,
                    Seeders = seeders,
                    Leechers = leechers,
                    PeersDiscovered = peersCount,
                    IsSuccess = success,
                    Operation = "Announce"
                });
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed inserting tracker metric snapshot");
            }

            return metric;
        }
    }

    public TrackerMetric RecordScrape(
        string trackerUrl,
        long responseTimeMs,
        bool success,
        int seeders,
        int leechers,
        int completed,
        string error = null)
    {
        if (string.IsNullOrWhiteSpace(trackerUrl))
        {
            return null;
        }

        lock (_lock)
        {
            var metric = GetOrCreateMetric(trackerUrl);
            var now = DateTime.UtcNow;

            metric.TotalScrapes++;
            metric.LastScrape = now;

            if (success)
            {
                UpdateLatencyMetrics(metric, responseTimeMs);
                metric.SuccessfulScrapes++;
                metric.ConsecutiveFailures = 0;
                metric.LastSuccess = now;
                metric.LastErrorMessage = null;
                metric.Status = "Working";
                if (seeders > 0)
                {
                    metric.LastSeeders = seeders;
                }

                if (leechers > 0)
                {
                    metric.LastLeechers = leechers;
                }
            }
            else
            {
                metric.FailedScrapes++;
                metric.LastErrorTime = now;
                metric.LastErrorMessage = error ?? "Scrape failed";
            }

            _metricRepository.Update(metric);
            _eventAggregator?.PublishEvent(new TrackerMetricUpdatedEvent(metric.TrackerUrl, metric.TotalAnnounces, metric.TotalUploaded, metric.TotalDownloaded, metric.Status));

            try
            {
                _snapshotRepository.Insert(new TrackerMetricSnapshot
                {
                    TrackerMetricId = metric.Id,
                    TrackerUrl = metric.TrackerUrl,
                    Timestamp = now,
                    ResponseTimeMs = responseTimeMs,
                    Uploaded = 0,
                    Downloaded = 0,
                    Seeders = seeders,
                    Leechers = leechers,
                    PeersDiscovered = 0,
                    IsSuccess = success,
                    Operation = "Scrape"
                });
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed inserting tracker scrape snapshot");
            }

            return metric;
        }
    }

    public List<TrackerMetric> GetAllMetrics()
    {
        var metrics = _metricRepository.All().OrderByDescending(m => m.TotalUploaded).ThenByDescending(m => m.TotalAnnounces).ToList();
        if (metrics.Count > 0)
        {
            try
            {
                var since = DateTime.UtcNow.AddHours(-24);
                var snapshots = _snapshotRepository.GetRecentSnapshots(since);
                var snapshotsByTracker = snapshots.GroupBy(s => s.TrackerMetricId).ToDictionary(g => g.Key, g => g.ToList());

                foreach (var metric in metrics)
                {
                    snapshotsByTracker.TryGetValue(metric.Id, out var trackerSnapshots);
                    PopulatePercentiles(metric, trackerSnapshots);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed populating tracker metric percentiles");
            }
        }

        return metrics;
    }

    public TrackerMetric GetMetric(int id)
    {
        var metric = _metricRepository.Get(id);
        if (metric != null)
        {
            try
            {
                var snapshots = _snapshotRepository.GetHistory(id, DateTime.UtcNow.AddHours(-24));
                PopulatePercentiles(metric, snapshots);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed populating tracker metric percentiles for metric {Id}", id);
            }
        }

        return metric;
    }

    public TrackerMetric GetMetricByUrl(string url)
    {
        var metric = _metricRepository.FindByUrl(url);
        if (metric != null)
        {
            try
            {
                var snapshots = _snapshotRepository.GetHistory(metric.Id, DateTime.UtcNow.AddHours(-24));
                PopulatePercentiles(metric, snapshots);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed populating tracker metric percentiles for url {Url}", url);
            }
        }

        return metric;
    }

    public List<TrackerMetricSnapshot> GetHistory(int id, int hours = 24)
    {
        var since = DateTime.UtcNow.AddHours(-Math.Max(1, hours));
        return _snapshotRepository.GetHistory(id, since);
    }

    public void ResetMetrics(int id)
    {
        _metricRepository.ResetStats(id);
    }

    public void DeleteMetric(int id)
    {
        _snapshotRepository.DeleteByMetricId(id);
        _metricRepository.Delete(id);
    }

    public TrackerMetricsSummary GetSummary()
    {
        var all = _metricRepository.All().ToList();
        var summary = new TrackerMetricsSummary
        {
            TotalTrackers = all.Count,
            HealthyTrackers = all.Count(m => m.Status == "Working"),
            DegradedTrackers = all.Count(m => m.Status == "Degraded"),
            OfflineTrackers = all.Count(m => m.Status == "Offline" || m.Status == "Failed"),
            TotalUploaded = all.Sum(m => m.TotalUploaded),
            TotalDownloaded = all.Sum(m => m.TotalDownloaded),
            TotalAnnounces = all.Sum(m => m.TotalAnnounces),
            SuccessfulAnnounces = all.Sum(m => m.SuccessfulAnnounces),
            FailedAnnounces = all.Sum(m => m.FailedAnnounces),
            TotalScrapes = all.Sum(m => m.TotalScrapes),
            SuccessfulScrapes = all.Sum(m => m.SuccessfulScrapes),
            TotalPeersDiscovered = all.Sum(m => m.TotalPeersDiscovered),
            AvgResponseTimeMs = all.Where(m => m.AvgResponseTimeMs > 0).Select(m => m.AvgResponseTimeMs).DefaultIfEmpty(0).Average()
        };

        // Protocol Breakdown
        foreach (var m in all)
        {
            var proto = (m.Protocol ?? "http").ToUpperInvariant();
            summary.ProtocolDistribution[proto] = summary.ProtocolDistribution.GetValueOrDefault(proto, 0) + 1;

            var status = m.Status ?? "Working";
            summary.HealthDistribution[status] = summary.HealthDistribution.GetValueOrDefault(status, 0) + 1;
        }

        // Top upload trackers
        summary.TopUploadTrackers = all
            .OrderByDescending(m => m.TotalUploaded)
            .Take(6)
            .Select(m => new TrackerMetricItemSummary
            {
                Id = m.Id,
                TrackerUrl = m.TrackerUrl,
                Domain = m.Domain,
                Protocol = m.Protocol,
                Status = m.Status,
                TotalUploaded = m.TotalUploaded,
                TotalDownloaded = m.TotalDownloaded,
                TotalPeersDiscovered = m.TotalPeersDiscovered,
                AvgResponseTimeMs = m.AvgResponseTimeMs,
                SuccessRate = m.TotalAnnounces > 0 ? Math.Round((double)m.SuccessfulAnnounces / m.TotalAnnounces * 100.0, 1) : 100.0
            })
            .ToList();

        // Top peer trackers
        summary.TopPeerTrackers = all
            .OrderByDescending(m => m.TotalPeersDiscovered)
            .Take(6)
            .Select(m => new TrackerMetricItemSummary
            {
                Id = m.Id,
                TrackerUrl = m.TrackerUrl,
                Domain = m.Domain,
                Protocol = m.Protocol,
                Status = m.Status,
                TotalUploaded = m.TotalUploaded,
                TotalDownloaded = m.TotalDownloaded,
                TotalPeersDiscovered = m.TotalPeersDiscovered,
                AvgResponseTimeMs = m.AvgResponseTimeMs,
                SuccessRate = m.TotalAnnounces > 0 ? Math.Round((double)m.SuccessfulAnnounces / m.TotalAnnounces * 100.0, 1) : 100.0
            })
            .ToList();

        // Hourly history points for last 24h aligned without gap loss
        try
        {
            var now = DateTime.UtcNow;
            var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
            var oldestBucketStart = currentHour.AddHours(-24);
            var snapshots = _snapshotRepository.GetRecentSnapshots(oldestBucketStart);

            var hourlyBuckets = new List<HourlyTrafficPoint>(24);
            for (var i = 0; i < 24; i++)
            {
                var bucketStart = oldestBucketStart.AddHours(i);
                var bucketEnd = bucketStart.AddHours(1);

                var inBucket = snapshots.Where(s => s.Timestamp >= bucketStart && s.Timestamp < bucketEnd).ToList();
                hourlyBuckets.Add(new HourlyTrafficPoint
                {
                    TimeLabel = bucketStart.ToString("HH:mm"),
                    Timestamp = bucketStart,
                    Uploaded = inBucket.Sum(s => s.Uploaded),
                    Downloaded = inBucket.Sum(s => s.Downloaded),
                    Announces = inBucket.Count(s => s.Operation == "Announce"),
                    PeersDiscovered = inBucket.Sum(s => s.PeersDiscovered),
                    AvgLatencyMs = inBucket.Where(s => s.ResponseTimeMs > 0).Select(s => (double)s.ResponseTimeMs).DefaultIfEmpty(0).Average()
                });
            }

            summary.HourlyHistory = hourlyBuckets;

            // SLA Latency Percentiles (P50, P95, P99)
            var validLatencies = snapshots
                .Where(s => s.IsSuccess && s.ResponseTimeMs > 0)
                .Select(s => (double)s.ResponseTimeMs)
                .ToList();

            if (validLatencies.Count > 0)
            {
                summary.LatencyP50Ms = CalculatePercentile(validLatencies, 50);
                summary.LatencyP95Ms = CalculatePercentile(validLatencies, 95);
                summary.LatencyP99Ms = CalculatePercentile(validLatencies, 99);
            }
            else
            {
                var trackerAverages = all.Where(m => m.AvgResponseTimeMs > 0).Select(m => m.AvgResponseTimeMs).ToList();
                summary.LatencyP50Ms = CalculatePercentile(trackerAverages, 50);
                summary.LatencyP95Ms = CalculatePercentile(trackerAverages, 95);
                summary.LatencyP99Ms = CalculatePercentile(trackerAverages, 99);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed generating hourly traffic history or SLA percentiles");
        }

        return summary;
    }

    private TrackerMetric GetOrCreateMetric(string url)
    {
        var trimmed = (url ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var existing = _metricRepository.FindByUrl(trimmed);
        if (existing != null)
        {
            return existing;
        }

        var (host, domain, proto, port) = ParseTrackerUrl(trimmed);
        var metric = new TrackerMetric
        {
            TrackerUrl = trimmed,
            Host = host,
            Domain = domain,
            Protocol = proto,
            Port = port,
            Status = "Working",
            FirstSeen = DateTime.UtcNow
        };

        return _metricRepository.Insert(metric);
    }

    private static (string Host, string Domain, string Protocol, int Port) ParseTrackerUrl(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var proto = uri.Scheme.ToLowerInvariant();
                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : (proto == "https" ? 443 : (proto == "udp" ? 1337 : 80));
                var domain = ExtractDomain(host);
                return (host, domain, proto, port);
            }
        }
        catch
        {
            // fallback
        }

        return (url, url, "http", 80);
    }

    private static string ExtractDomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "Unknown";
        }

        var parts = host.Split('.');
        if (parts.Length >= 2)
        {
            return string.Join('.', parts.TakeLast(2));
        }

        return host;
    }

    public static void UpdateLatencyMetrics(TrackerMetric metric, long responseTimeMs)
    {
        if (metric == null)
        {
            return;
        }

        metric.LastResponseTimeMs = responseTimeMs;

        if (responseTimeMs > 0)
        {
            if (metric.MinResponseTimeMs == 0 || responseTimeMs < metric.MinResponseTimeMs)
            {
                metric.MinResponseTimeMs = responseTimeMs;
            }

            if (responseTimeMs > metric.MaxResponseTimeMs)
            {
                metric.MaxResponseTimeMs = responseTimeMs;
            }

            // Running exponential moving average
            if (metric.AvgResponseTimeMs <= 0)
            {
                metric.AvgResponseTimeMs = responseTimeMs;
            }
            else
            {
                metric.AvgResponseTimeMs = Math.Round((metric.AvgResponseTimeMs * 0.85) + (responseTimeMs * 0.15), 1);
            }
        }
    }

    public static double CalculatePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values == null || values.Count == 0)
        {
            return 0.0;
        }

        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 1)
        {
            return Math.Round(list[0], 2);
        }

        if (percentile <= 0)
        {
            return Math.Round(list[0], 2);
        }

        if (percentile >= 100)
        {
            return Math.Round(list[^1], 2);
        }

        var rank = (percentile / 100.0) * (list.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);

        if (lowerIndex == upperIndex)
        {
            return Math.Round(list[lowerIndex], 2);
        }

        var fraction = rank - lowerIndex;
        var value = list[lowerIndex] + (fraction * (list[upperIndex] - list[lowerIndex]));
        return Math.Round(value, 2);
    }

    public static double CalculatePercentile(IEnumerable<double> values, double percentile)
    {
        if (values == null)
        {
            return 0.0;
        }

        var list = values.ToList();
        return CalculatePercentile((IReadOnlyList<double>)list, percentile);
    }

    public static double CalculatePercentile(IEnumerable<long> values, double percentile)
    {
        if (values == null)
        {
            return 0.0;
        }

        return CalculatePercentile(values.Select(v => (double)v), percentile);
    }

    public static void PopulatePercentiles(TrackerMetric metric, IEnumerable<TrackerMetricSnapshot> snapshots)
    {
        if (metric == null)
        {
            return;
        }

        var validLatencies = snapshots?
            .Where(s => s.IsSuccess && s.ResponseTimeMs > 0)
            .Select(s => (double)s.ResponseTimeMs)
            .ToList();

        if (validLatencies != null && validLatencies.Count > 0)
        {
            metric.LatencyP50Ms = CalculatePercentile(validLatencies, 50);
            metric.LatencyP95Ms = CalculatePercentile(validLatencies, 95);
            metric.LatencyP99Ms = CalculatePercentile(validLatencies, 99);
        }
        else if (metric.AvgResponseTimeMs > 0)
        {
            metric.LatencyP50Ms = metric.AvgResponseTimeMs;
            metric.LatencyP95Ms = metric.MaxResponseTimeMs > 0 ? metric.MaxResponseTimeMs : metric.AvgResponseTimeMs;
            metric.LatencyP99Ms = metric.MaxResponseTimeMs > 0 ? metric.MaxResponseTimeMs : metric.AvgResponseTimeMs;
        }
        else
        {
            metric.LatencyP50Ms = 0;
            metric.LatencyP95Ms = 0;
            metric.LatencyP99Ms = 0;
        }
    }
}
