namespace NzbDrone.Core.Datastore;

public class DatabaseMaintenanceResult
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
