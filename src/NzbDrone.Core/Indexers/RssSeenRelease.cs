using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public enum RssSeenStatus
{
    Grabbed = 1,
    Ignored = 2,
    Rejected = 3
}

public class RssSeenRelease : ModelBase
{
    public int IndexerId { get; set; }
    public string Guid { get; set; }
    public string InfoHash { get; set; }
    public DateTime? PublishDate { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public RssSeenStatus Status { get; set; }
    public int? MatchedRuleId { get; set; }
}
