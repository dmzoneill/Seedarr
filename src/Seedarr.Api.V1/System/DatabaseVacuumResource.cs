using Seedarr.Http.REST;

namespace Seedarr.Api.V1.System;

/// <summary>
/// API resource representing database vacuum and defragmentation maintenance results.
/// </summary>
public class DatabaseVacuumResource : RestResource
{
    public bool Success { get; set; }
    public string DatabaseType { get; set; }
    public long InitialFreelistPages { get; set; }
    public long FinalFreelistPages { get; set; }
    public long ReclaimedPages { get; set; }
    public long PageSize { get; set; }
    public long ReclaimedBytes { get; set; }
    public string Message { get; set; }
}
