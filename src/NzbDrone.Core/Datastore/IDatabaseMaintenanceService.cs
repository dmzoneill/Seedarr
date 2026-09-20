namespace NzbDrone.Core.Datastore;

public interface IDatabaseMaintenanceService
{
    DatabaseMaintenanceResult PerformMaintenance(int? maxPages = null);
    DatabaseMaintenanceResult Vacuum(int? maxPages = null);
    long GetFreelistCount();
    long GetFreelistBytes();
    long GetPageSize();
    bool IsIncrementalAutoVacuumEnabled();
}
