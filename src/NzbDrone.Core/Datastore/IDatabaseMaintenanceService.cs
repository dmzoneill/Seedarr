namespace NzbDrone.Core.Datastore;

public interface IDatabaseMaintenanceService
{
    DatabaseMaintenanceResult PerformMaintenance(int? maxPages = null);
    DatabaseMaintenanceResult Vacuum(int? maxPages = null);
    DatabaseMaintenanceResult CheckpointWal(WalCheckpointMode mode = WalCheckpointMode.Passive);
    long GetFreelistCount();
    long GetFreelistBytes();
    long GetPageSize();
    bool IsIncrementalAutoVacuumEnabled();
}
