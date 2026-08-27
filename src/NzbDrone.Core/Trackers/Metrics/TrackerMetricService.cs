using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
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
    private readonly Logger _logger;
    private readonly object _lock = new();

    public TrackerMetricService(
        ITrackerMetricRepository metricRepository,
        ITrackerMetricSnapshotRepository snapshotRepository,
        ITrackerEntryRepository trackerEntryRepository,
        ITorrentRepository torrentRepository)
    {
        _metricRepository = metricRepository;
        _snapshotRepository = snapshotRepository;
        _trackerEntryRepository = trackerEntryRepository;
        _torrentRepository = torrentRepository;
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
            metric.LastResponseTimeMs = responseTimeMs;

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

            if (success)
            {
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
            }

            _metricRepository.Update(metric);

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
            metric.LastResponseTimeMs = responseTimeMs;

            if (success)
            {
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
        return _metricRepository.All().OrderByDescending(m => m.TotalUploaded).ThenByDescending(m => m.TotalAnnounces).ToList();
    }

    public TrackerMetric GetMetric(int id)
    {
        return _metricRepository.Get(id);
    }

    public TrackerMetric GetMetricByUrl(string url)
    {
        return _metricRepository.FindByUrl(url);
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

        // Hourly history points for last 24h
        try
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var snapshots = _snapshotRepository.GetRecentSnapshots(since);

            var hourlyBuckets = new List<HourlyTrafficPoint>();
            for (var i = 23; i >= 0; i--)
            {
                var hourStart = DateTime.UtcNow.AddHours(-i);
                var bucketStart = new DateTime(hourStart.Year, hourStart.Month, hourStart.Day, hourStart.Hour, 0, 0, DateTimeKind.Utc);
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
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed generating hourly traffic history");
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
}
