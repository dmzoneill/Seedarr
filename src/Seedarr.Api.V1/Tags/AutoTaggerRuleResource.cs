using NzbDrone.Core.Tags;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Tags;

public class AutoTaggerRuleResource : RestResource
{
    public string Name { get; set; }
    public int TagId { get; set; }
    public string TagLabel { get; set; }
    public AutoTaggerRuleType RuleType { get; set; }
    public string Pattern { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
}
