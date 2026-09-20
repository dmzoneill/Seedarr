using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public class RssGrabHistory : ModelBase
{
    public const string StatusGrabbed = "Grabbed";
    public const string StatusFailed = "Failed";

    public string ReleaseTitle { get; set; }
    public string IndexerName { get; set; }
    public int? RuleId { get; set; }
    public string RuleName { get; set; }
    public string InfoHash { get; set; }
    public long Size { get; set; }
    public DateTime GrabTimestamp { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = StatusGrabbed;
    public string ErrorMessage { get; set; }
}
