using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Indexers;

public interface IRssSyncService
{
    bool MatchesRule(RssRule rule, ReleaseInfo release, DateTime? now = null);

    List<ReleaseInfo> FilterReleases(IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null);
}

public class RssSyncService : IRssSyncService
{
    private readonly IRssRuleEvaluator _ruleEvaluator;

    public RssSyncService(IRssRuleEvaluator ruleEvaluator = null)
    {
        _ruleEvaluator = ruleEvaluator ?? new RssRuleEvaluator();
    }

    public bool MatchesRule(RssRule rule, ReleaseInfo release, DateTime? now = null)
    {
        if (rule == null || release == null)
        {
            return false;
        }

        return _ruleEvaluator.Matches(rule, release, now);
    }

    public List<ReleaseInfo> FilterReleases(IEnumerable<ReleaseInfo> releases, IEnumerable<RssRule> rules, DateTime? now = null)
    {
        var matched = new List<ReleaseInfo>();
        if (releases == null || rules == null)
        {
            return matched;
        }

        var enabledRules = rules.Where(r => r != null && r.IsEnabled).ToList();
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
}
