using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.Metrics;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Trackers;

public class TrackerScrapeService : ITrackerScrapeService
{
    public static readonly TimeSpan DefaultCycleTimeout = TimeSpan.FromMinutes(8);
    private const int MaxConcurrentTorrents = 4;

    private readonly IMultiTrackerManager _multiTracker;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly ITorrentService _torrentService;
    private readonly ITrackerMetricService _trackerMetricService;
    private readonly Logger _logger;

    public TrackerScrapeService(
        IMultiTrackerManager multiTracker,
        ITrackerEntryService trackerEntryService,
        ITorrentService torrentService,
        ITrackerMetricService trackerMetricService = null)
    {
        _multiTracker = multiTracker;
        _trackerEntryService = trackerEntryService;
        _torrentService = torrentService;
        _trackerMetricService = trackerMetricService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    internal TimeSpan CycleTimeout { get; set; } = DefaultCycleTimeout;

    public async Task<TrackerScrapeResult> ScrapeTorrentAsync(
        int torrentId,
        CancellationToken cancellationToken = default)
    {
        var torrent = _torrentService.Get(torrentId);
        if (torrent == null)
        {
            _logger.Warn("Cannot scrape torrent {0}: torrent not found", torrentId);
            return new TrackerScrapeResult
            {
                TorrentId = torrentId,
                Success = false,
                Status = "NotFound",
                ErrorMessage = $"Torrent {torrentId} not found"
            };
        }

        if (string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            _logger.Warn("Cannot scrape torrent {0}: missing InfoHash", torrentId);
            return new TrackerScrapeResult
            {
                TorrentId = torrentId,
                Success = false,
                Status = "InvalidTorrent",
                ErrorMessage = "Torrent has no valid InfoHash"
            };
        }

        var trackerEntries = _trackerEntryService.GetByTorrentId(torrentId) ?? new List<TrackerEntry>();
        var activeEntries = trackerEntries.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Url)).ToList();

        if (activeEntries.Count == 0 && !string.IsNullOrWhiteSpace(torrent.TrackerUrl))
        {
            var entry = new TrackerEntry
            {
                TorrentId = torrentId,
                Url = torrent.TrackerUrl,
                Tier = 0,
                Status = TrackerStatus.Unknown,
                Enabled = true
            };

            try
            {
                entry = _trackerEntryService.Add(entry);
                activeEntries.Add(entry);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to persist fallback tracker entry for torrent {0}", torrentId);
            }
        }

        if (activeEntries.Count == 0)
        {
            return new TrackerScrapeResult
            {
                TorrentId = torrentId,
                Success = false,
                Status = "NoTrackers",
                ErrorMessage = "No active tracker entries found for torrent",
                Seeders = torrent.Seeders,
                Leechers = torrent.Leechers
            };
        }

        var itemResults = new List<TrackerScrapeItemResult>();

        foreach (var entry in activeEntries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var sw = Stopwatch.StartNew();
            TrackerScrapeResponse response = null;
            string error = null;

            try
            {
                response = await _multiTracker.ScrapeAsync(entry, torrent.InfoHash, cancellationToken);
                if (response == null || !response.Success)
                {
                    error = response?.FailureReason ?? "Scrape failed";
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Exception scraping tracker {0} for torrent {1}", entry.Url, torrentId);
                error = ex.Message;
                response = new TrackerScrapeResponse
                {
                    Success = false,
                    FailureReason = ex.Message
                };
            }
            finally
            {
                sw.Stop();
            }

            var responseTimeMs = sw.ElapsedMilliseconds;
            var isSuccess = response != null && response.Success;

            entry.LastScrape = DateTime.UtcNow;
            entry.LastResponseTime = responseTimeMs;

            if (isSuccess)
            {
                entry.Seeders = response.Complete;
                entry.Leechers = response.Incomplete;
                if (response.Downloaded > 0)
                {
                    entry.Downloaded = response.Downloaded;
                }

                entry.ConsecutiveFailures = 0;
                entry.ErrorMessage = null;
                entry.Status = TrackerStatus.Working;

                _trackerMetricService?.RecordScrape(
                    entry.Url,
                    responseTimeMs,
                    true,
                    response.Complete,
                    response.Incomplete,
                    response.Downloaded);
            }
            else
            {
                entry.ConsecutiveFailures++;
                entry.LastErrorTime = DateTime.UtcNow;
                entry.ErrorMessage = error;
                if (entry.ConsecutiveFailures >= 3)
                {
                    entry.Status = TrackerStatus.Failed;
                }

                _trackerMetricService?.RecordScrape(
                    entry.Url,
                    responseTimeMs,
                    false,
                    entry.Seeders,
                    entry.Leechers,
                    0,
                    error);
            }

            try
            {
                _trackerEntryService.Update(entry);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to update tracker entry {0} for torrent {1}", entry.Id, torrentId);
            }

            itemResults.Add(new TrackerScrapeItemResult
            {
                TrackerId = entry.Id,
                Url = entry.Url,
                Success = isSuccess,
                Seeders = entry.Seeders,
                Leechers = entry.Leechers,
                Completed = isSuccess ? response.Downloaded : (int)entry.Downloaded,
                ResponseTimeMs = responseTimeMs,
                ErrorMessage = isSuccess ? null : error
            });
        }

        var successfulScrapes = itemResults.Count(r => r.Success);
        var failedScrapes = itemResults.Count(r => !r.Success);
        var overallSuccess = successfulScrapes > 0;

        if (overallSuccess)
        {
            var maxSeeders = activeEntries.Max(e => e.Seeders);
            var maxLeechers = activeEntries.Max(e => e.Leechers);

            torrent.Seeders = maxSeeders;
            torrent.Leechers = maxLeechers;

            if (maxSeeders > 0)
            {
                torrent.Availability = Math.Max(torrent.Availability, (double)maxSeeders);
            }
            else if (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding)
            {
                torrent.Availability = Math.Max(1.0, torrent.Availability);
            }

            try
            {
                _torrentService.UpdateSwarmStats(torrent.Id, torrent.Seeders, torrent.Leechers, torrent.Availability);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to update torrent metrics for torrent {0}", torrentId);
            }
        }

        return new TrackerScrapeResult
        {
            TorrentId = torrentId,
            Success = overallSuccess,
            Seeders = torrent.Seeders,
            Leechers = torrent.Leechers,
            Completed = itemResults.Where(r => r.Success).Select(r => r.Completed).DefaultIfEmpty(0).Max(),
            Status = overallSuccess ? "Working" : "Failed",
            TrackersScraped = itemResults.Count,
            SuccessfulScrapes = successfulScrapes,
            FailedScrapes = failedScrapes,
            ErrorMessage = overallSuccess ? null : string.Join("; ", itemResults.Where(r => !r.Success).Select(r => $"{r.Url}: {r.ErrorMessage}")),
            TrackerResults = itemResults
        };
    }

    public async Task<int> ScrapeAllTorrentsAsync(CancellationToken cancellationToken = default)
    {
        var torrents = _torrentService.GetAll() ?? new List<Torrent>();
        _logger.Info("Starting tracker scrape for all {0} torrents", torrents.Count);

        using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (CycleTimeout > TimeSpan.Zero)
        {
            cycleCts.CancelAfter(CycleTimeout);
        }

        var cycleToken = cycleCts.Token;
        var scrapedCount = 0;
        using var semaphore = new SemaphoreSlim(MaxConcurrentTorrents);
        var tasks = new List<Task>(torrents.Count);

        foreach (var torrent in torrents)
        {
            if (cycleToken.IsCancellationRequested)
            {
                break;
            }

            tasks.Add(ScrapeSingleTorrentAsync(torrent));
        }

        await Task.WhenAll(tasks);

        if (cancellationToken.IsCancellationRequested)
        {
            _logger.Info("ScrapeAllTorrentsAsync cancelled after {0} torrents", scrapedCount);
        }
        else if (cycleToken.IsCancellationRequested)
        {
            _logger.Warn(
                "ScrapeAllTorrentsAsync reached cycle timeout ({0:N0}s) after {1} of {2} torrents",
                CycleTimeout.TotalSeconds,
                scrapedCount,
                torrents.Count);
        }

        _logger.Info("Finished tracker scrape for {0} torrents", scrapedCount);
        return scrapedCount;

        async Task ScrapeSingleTorrentAsync(Torrent torrent)
        {
            try
            {
                await semaphore.WaitAsync(cycleToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (cycleToken.IsCancellationRequested)
                {
                    return;
                }

                await ScrapeTorrentAsync(torrent.Id, cycleToken);
                Interlocked.Increment(ref scrapedCount);
            }
            catch (OperationCanceledException) when (cycleToken.IsCancellationRequested)
            {
                // Bounded cycle or caller cancellation
            }
            catch (Exception ex)
            {
                _logger.Warn(
                    ex,
                    "Failed to scrape torrent {0} ({1}); continuing with remaining torrents",
                    torrent.Id,
                    torrent.Name);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
