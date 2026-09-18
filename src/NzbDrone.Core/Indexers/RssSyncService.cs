using System;
using System.Collections.Generic;
using System.Linq;

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
}

public class RssSyncService : IRssSyncService
{
    private readonly IRssRuleEvaluator _ruleEvaluator;
    private readonly IIndexerStatusService _indexerStatusService;
    private readonly IRssSeenReleaseRepository _seenReleaseRepository;

    public RssSyncService(
        IRssRuleEvaluator ruleEvaluator = null,
        IIndexerStatusService indexerStatusService = null,
        IRssSeenReleaseRepository seenReleaseRepository = null)
    {
        _ruleEvaluator = ruleEvaluator ?? new RssRuleEvaluator();
        _indexerStatusService = indexerStatusService;
        _seenReleaseRepository = seenReleaseRepository;
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
}
