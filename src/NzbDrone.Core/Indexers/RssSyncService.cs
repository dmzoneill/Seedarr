using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Indexers;

public interface IRssSyncService
{
    bool MatchesRule(RssRule rule, ReleaseInfo release, DateTime? now = null);

    RssRule GetFirstMatchingRule(IEnumerable<RssRule> rules, ReleaseInfo release, DateTime? now = null);

    List<ReleaseInfo> FilterReleases(IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null);

    bool ShouldSyncIndexer(IndexerDefinition indexer, bool isManual = false, DateTime? now = null);

    List<IndexerDefinition> FilterEligibleIndexers(IEnumerable<IndexerDefinition> indexers, bool isManual = false, DateTime? now = null);
}

public class RssSyncService : IRssSyncService
{
    private readonly IRssRuleEvaluator _ruleEvaluator;
    private readonly IIndexerStatusService _indexerStatusService;

    public RssSyncService(IRssRuleEvaluator ruleEvaluator = null, IIndexerStatusService indexerStatusService = null)
    {
        _ruleEvaluator = ruleEvaluator ?? new RssRuleEvaluator();
        _indexerStatusService = indexerStatusService;
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
