using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tags;

public class AutoTaggerRule : ModelBase
{
    public string Name { get; set; }
    public int TagId { get; set; }
    public AutoTaggerRuleType RuleType { get; set; }
    public string Pattern { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
}
