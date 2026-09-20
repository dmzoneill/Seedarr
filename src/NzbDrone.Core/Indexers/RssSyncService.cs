using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Indexers;

public interface IRssSyncService
{
    bool MatchesRule(RssRule rule, ReleaseInfo release, DateTime? now = null);

    RssRule GetFirstMatchingRule(IEnumerable<RssRule> rules, ReleaseInfo release, DateTime? now = null);

    List<ReleaseInfo> FilterReleases(IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null);

    List<ReleaseInfo> FilterReleases(int indexerId, IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null);

    bool ShouldSyncIndexer(IndexerDefinition indexer, bool isManual = false, DateTime? now = null);

    List<IndexerDefinition> FilterEligibleIndexers(IEnumerable<IndexerDefinition> indexers, bool isManual = false, DateTime? now = null);

    List<ReleaseInfo> FilterNewReleases(int indexerId, IEnumerable<ReleaseInfo> releases);

    void RecordSeen(int indexerId, ReleaseInfo release, RssSeenStatus status, int? matchedRuleId = null);

    void RecordSeenBatch(int indexerId, IEnumerable<ReleaseInfo> releases, RssSeenStatus status);

    int PurgeSeenReleases(TimeSpan age);

    RssGrabResult GrabRelease(ReleaseInfo release, RssRule rule, string indexerName = null);
}

public class RssGrabResult
{
    public bool Success { get; set; }
    public Torrent Torrent { get; set; }
    public RssGrabHistory GrabHistory { get; set; }
    public string ErrorMessage { get; set; }
}

public class RssSyncService : IRssSyncService
{
    private readonly IRssRuleEvaluator _ruleEvaluator;
    private readonly IIndexerStatusService _indexerStatusService;
    private readonly IRssSeenReleaseRepository _seenReleaseRepository;
    private readonly ITorrentService _torrentService;
    private readonly IRssGrabHistoryRepository _grabHistoryRepository;

    public RssSyncService(
        IRssRuleEvaluator ruleEvaluator = null,
        IIndexerStatusService indexerStatusService = null,
        IRssSeenReleaseRepository seenReleaseRepository = null,
        ITorrentService torrentService = null,
        IRssGrabHistoryRepository grabHistoryRepository = null)
    {
        _ruleEvaluator = ruleEvaluator ?? new RssRuleEvaluator();
        _indexerStatusService = indexerStatusService;
        _seenReleaseRepository = seenReleaseRepository;
        _torrentService = torrentService;
        _grabHistoryRepository = grabHistoryRepository;
    }

    public bool MatchesRule(RssRule rule, ReleaseInfo release, DateTime? now = null)
    {
        if (rule == null || release == null)
        {
            return false;
        }

        return _ruleEvaluator.Matches(rule, release, now);
    }

    public RssRule GetFirstMatchingRule(IEnumerable<RssRule> rules, ReleaseInfo release, DateTime? now = null)
    {
        if (rules == null || release == null)
        {
            return null;
        }

        var enabledRules = rules
            .Where(r => r != null && r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToList();

        foreach (var rule in enabledRules)
        {
            if (MatchesRule(rule, release, now))
            {
                return rule;
            }
        }

        return null;
    }

    public List<ReleaseInfo> FilterReleases(IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null)
    {
        var matched = new List<ReleaseInfo>();
        if (releases == null || rules == null)
        {
            return matched;
        }

        var enabledRules = rules
            .Where(r => r != null && r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToList();

        if (enabledRules.Count == 0)
        {
            return matched;
        }

        foreach (var release in releases)
        {
            if (release == null)
            {
                continue;
            }

            foreach (var rule in enabledRules)
            {
                if (MatchesRule(rule, release, now))
                {
                    matched.Add(release);
                    break;
                }
            }
        }

        return matched;
    }

    public List<ReleaseInfo> FilterReleases(int indexerId, IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null)
    {
        var newReleases = FilterNewReleases(indexerId, releases);
        return FilterReleases(newReleases, rules, now);
    }

    public List<ReleaseInfo> FilterNewReleases(int indexerId, IEnumerable<ReleaseInfo> releases)
    {
        if (releases == null)
        {
            return new List<ReleaseInfo>();
        }

        if (_seenReleaseRepository == null)
        {
            return releases.Where(r => r != null).ToList();
        }

        var seenGuids = _seenReleaseRepository.GetSeenGuids(indexerId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHashes = _seenReleaseRepository.GetSeenInfoHashes(indexerId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = new List<ReleaseInfo>();
        var batchGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batchHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var release in releases)
        {
            if (release == null)
            {
                continue;
            }

            var guid = !string.IsNullOrWhiteSpace(release.Guid)
                ? release.Guid.Trim()
                : (!string.IsNullOrWhiteSpace(release.DownloadUrl) ? release.DownloadUrl.Trim() : string.Empty);
            var infoHash = string.IsNullOrWhiteSpace(release.InfoHash) ? null : release.InfoHash.Trim().ToLowerInvariant();

            var hasGuid = !string.IsNullOrWhiteSpace(guid);
            var hasInfoHash = !string.IsNullOrWhiteSpace(infoHash);

            if (!hasGuid && !hasInfoHash)
            {
                result.Add(release);
                continue;
            }

            if (hasGuid && (seenGuids.Contains(guid) || batchGuids.Contains(guid)))
            {
                continue;
            }

            if (hasInfoHash && (seenHashes.Contains(infoHash) || batchHashes.Contains(infoHash)))
            {
                continue;
            }

            if (hasGuid)
            {
                batchGuids.Add(guid);
            }

            if (hasInfoHash)
            {
                batchHashes.Add(infoHash);
            }

            result.Add(release);
        }

        return result;
    }

    public void RecordSeen(int indexerId, ReleaseInfo release, RssSeenStatus status, int? matchedRuleId = null)
    {
        if (release == null || _seenReleaseRepository == null)
        {
            return;
        }

        _seenReleaseRepository.MarkSeen(indexerId, release, status, matchedRuleId);
    }

    public void RecordSeenRelease(int indexerId, ReleaseInfo release, RssSeenStatus status, int? matchedRuleId = null)
    {
        RecordSeen(indexerId, release, status, matchedRuleId);
    }

    public void RecordSeenBatch(int indexerId, IEnumerable<ReleaseInfo> releases, RssSeenStatus status)
    {
        if (releases == null || _seenReleaseRepository == null)
        {
            return;
        }

        _seenReleaseRepository.MarkSeenBatch(indexerId, releases, status);
    }

    public void RecordSeenReleases(int indexerId, IEnumerable<ReleaseInfo> releases, RssSeenStatus status)
    {
        RecordSeenBatch(indexerId, releases, status);
    }

    public int PurgeSeenReleases(TimeSpan age)
    {
        return _seenReleaseRepository?.PurgeOlderThan(age) ?? 0;
    }

    public bool ShouldSyncIndexer(IndexerDefinition indexer, bool isManual = false, DateTime? now = null)
    {
        if (indexer == null || !indexer.Enable || !indexer.EnableRss)
        {
            return false;
        }

        if (isManual)
        {
            return true;
        }

        if (_indexerStatusService == null)
        {
            return true;
        }

        if (_indexerStatusService.IsDisabled(indexer.Id))
        {
            return false;
        }

        var status = _indexerStatusService.GetStatus(indexer.Id);
        var current = now ?? DateTime.UtcNow;
        if (status.NextRssSyncTimeUtc.HasValue && status.NextRssSyncTimeUtc.Value > current)
        {
            return false;
        }

        return true;
    }

    public List<IndexerDefinition> FilterEligibleIndexers(IEnumerable<IndexerDefinition> indexers, bool isManual = false, DateTime? now = null)
    {
        if (indexers == null)
        {
            return new List<IndexerDefinition>();
        }

        return indexers.Where(i => ShouldSyncIndexer(i, isManual, now)).ToList();
    }

    public RssGrabResult GrabRelease(ReleaseInfo release, RssRule rule, string indexerName = null)
    {
        if (release == null || rule == null)
        {
            return new RssGrabResult { Success = false, ErrorMessage = "Release or rule cannot be null" };
        }

        var history = new RssGrabHistory
        {
            ReleaseTitle = release.Title,
            IndexerName = !string.IsNullOrWhiteSpace(indexerName) ? indexerName : release.Indexer,
            RuleId = rule.Id,
            RuleName = rule.Name,
            InfoHash = release.InfoHash,
            Size = release.Size,
            GrabTimestamp = DateTime.UtcNow
        };

        try
        {
            if (_torrentService == null)
            {
                history.Status = RssGrabHistory.StatusFailed;
                history.ErrorMessage = "Torrent service is not configured";
                _grabHistoryRepository?.Insert(history);
                return new RssGrabResult { Success = false, GrabHistory = history, ErrorMessage = history.ErrorMessage };
            }

            var infoHash = release.InfoHash;
            string trackerUrl = null;
            if (!string.IsNullOrWhiteSpace(release.MagnetUrl) || (!string.IsNullOrWhiteSpace(release.DownloadUrl) && release.DownloadUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)))
            {
                var magnetStr = !string.IsNullOrWhiteSpace(release.MagnetUrl) ? release.MagnetUrl : release.DownloadUrl;
                try
                {
                    var parsedMagnet = MagnetLinkParser.Parse(magnetStr);
                    if (string.IsNullOrWhiteSpace(infoHash))
                    {
                        infoHash = parsedMagnet.InfoHash;
                    }

                    if (parsedMagnet.Trackers.Length > 0)
                    {
                        trackerUrl = parsedMagnet.Trackers[0];
                    }
                }
                catch
                {
                    // Fallback if parsing fails
                }
            }

            history.InfoHash = infoHash;

            if (!string.IsNullOrWhiteSpace(infoHash) && _torrentService.ExistsByInfoHash(infoHash))
            {
                history.Status = RssGrabHistory.StatusFailed;
                history.ErrorMessage = "Torrent with this info hash already exists in active library";
                _grabHistoryRepository?.Insert(history);
                RecordSeen(release.IndexerId, release, RssSeenStatus.Grabbed, rule.Id);
                return new RssGrabResult { Success = false, GrabHistory = history, ErrorMessage = history.ErrorMessage };
            }

            var torrent = new Torrent
            {
                Name = release.Title,
                InfoHash = infoHash,
                TrackerUrl = trackerUrl,
                TotalSize = release.Size,
                DateAdded = DateTime.UtcNow,
                SavePath = rule.SavePath,
                TagIds = rule.Tags != null ? new List<int>(rule.Tags) : new List<int>(),
                SequentialDownload = rule.SequentialDownload,
                Status = rule.InitialStatus ?? TorrentStatus.Queued,
                Category = rule.CategoryId > 0 ? rule.CategoryId.ToString() : null,
                Seeders = release.Seeders ?? 0,
                Leechers = release.Leechers ?? 0
            };

            var added = _torrentService.Add(torrent);

            history.Status = RssGrabHistory.StatusGrabbed;
            _grabHistoryRepository?.Insert(history);
            RecordSeen(release.IndexerId, release, RssSeenStatus.Grabbed, rule.Id);

            return new RssGrabResult
            {
                Success = true,
                Torrent = added,
                GrabHistory = history
            };
        }
        catch (Exception ex)
        {
            history.Status = RssGrabHistory.StatusFailed;
            history.ErrorMessage = ex.Message;
            _grabHistoryRepository?.Insert(history);
            RecordSeen(release.IndexerId, release, RssSeenStatus.Rejected, rule.Id);

            return new RssGrabResult
            {
                Success = false,
                GrabHistory = history,
                ErrorMessage = ex.Message
            };
        }
    }
}
