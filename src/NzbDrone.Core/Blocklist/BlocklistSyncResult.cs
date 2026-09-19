using System;
using System.Net;

namespace NzbDrone.Core.Blocklist;

public class BlocklistSyncResult
{
    public bool Success { get; set; }

    public string Status { get; set; }

    public HttpStatusCode? HttpStatusCode { get; set; }

    public string Message { get; set; }

    public int RuleCount { get; set; }

    public DateTime? NextAllowedSyncUtc { get; set; }

    public bool IsNotModified { get; set; }

    public bool IsRateLimited { get; set; }
}
