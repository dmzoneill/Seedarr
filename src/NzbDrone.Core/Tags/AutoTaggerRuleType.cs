namespace NzbDrone.Core.Tags;

public enum AutoTaggerRuleType
{
    Regex = 0,
    TrackerDomain = 1,
    Quality = 2,
    MediaInfo = 3,
    Size = 4
}
