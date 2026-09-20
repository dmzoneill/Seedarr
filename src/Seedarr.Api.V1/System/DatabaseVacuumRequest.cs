namespace Seedarr.Api.V1.System;

/// <summary>
/// Request parameters for triggering manual database vacuum.
/// </summary>
public class DatabaseVacuumRequest
{
    /// <summary>
    /// Optional maximum number of freelist pages to reclaim (default 5000).
    /// </summary>
    public int? MaxPages { get; set; }
}
