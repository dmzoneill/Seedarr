namespace NzbDrone.Core.RemotePathMappings;

public class RemotePathMappingTestResult
{
    public string InputPath { get; set; }

    public string MappedPath { get; set; }

    public bool RuleApplied { get; set; }

    public int? MatchedRuleId { get; set; }

    public string MatchedRuleHost { get; set; }

    public string MatchedRemotePrefix { get; set; }

    public string MatchedLocalPrefix { get; set; }

    public bool LocalPathExists { get; set; }
}
