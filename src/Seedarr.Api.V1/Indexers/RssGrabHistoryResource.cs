using System;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Indexers;

public class RssGrabHistoryResource : RestResource
{
    public string ReleaseTitle { get; set; }

    public string IndexerName { get; set; }

    public int? RuleId { get; set; }

    public string RuleName { get; set; }

    public string InfoHash { get; set; }

    public long Size { get; set; }

    public DateTime GrabTimestamp { get; set; }

    public string Status { get; set; }

    public string ErrorMessage { get; set; }
}
