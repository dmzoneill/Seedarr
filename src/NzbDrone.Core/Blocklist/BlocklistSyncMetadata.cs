using System;
using System.Net;

namespace NzbDrone.Core.Blocklist;

public class BlocklistSyncMetadata
{
    public string BlocklistETag { get; set; }

    public DateTimeOffset? BlocklistLastModified { get; set; }

    public DateTime? LastCheckedUtc { get; set; }

    public string LastSyncStatus { get; set; } = "Never Run";

    public HttpStatusCode? LastSyncHttpStatus { get; set; }

    public DateTime? NextAllowedSyncUtc { get; set; }

    public int ConsecutiveFailures { get; set; }

    public int RuleCount { get; set; }

    public string LastFailureMessage { get; set; }
}
