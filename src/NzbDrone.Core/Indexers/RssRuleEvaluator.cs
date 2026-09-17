using System;

namespace NzbDrone.Core.Indexers;

public interface IRssRuleEvaluator
{
    bool Matches(RssRule rule, ReleaseInfo release, DateTime? now = null);
}

public class RssRuleEvaluator : IRssRuleEvaluator
{
    public bool Matches(RssRule rule, ReleaseInfo release, DateTime? now = null)
    {
        if (rule == null || release == null)
        {
            return false;
        }

        return rule.Matches(release, now);
    }
}
