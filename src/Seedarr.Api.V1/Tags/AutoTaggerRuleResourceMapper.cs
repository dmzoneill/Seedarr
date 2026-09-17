using NzbDrone.Core.Tags;

namespace Seedarr.Api.V1.Tags;

public static class AutoTaggerRuleResourceMapper
{
    public static AutoTaggerRuleResource ToResource(AutoTaggerRule model, string tagLabel = null)
    {
        if (model == null)
        {
            return null;
        }

        return new AutoTaggerRuleResource
        {
            Id = model.Id,
            Name = model.Name,
            TagId = model.TagId,
            TagLabel = tagLabel,
            RuleType = model.RuleType,
            Pattern = model.Pattern,
            IsEnabled = model.IsEnabled,
            Priority = model.Priority
        };
    }

    public static AutoTaggerRule ToModel(AutoTaggerRuleResource resource)
    {
        if (resource == null)
        {
            return null;
        }

        return new AutoTaggerRule
        {
            Id = resource.Id,
            Name = resource.Name,
            TagId = resource.TagId,
            RuleType = resource.RuleType,
            Pattern = resource.Pattern,
            IsEnabled = resource.IsEnabled,
            Priority = resource.Priority
        };
    }
}
