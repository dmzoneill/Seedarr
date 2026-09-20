using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Jobs;

public class DatabaseVacuumTask : IScheduledTask, IExecute<DatabaseVacuumCommand>
{
    private readonly IDatabaseMaintenanceService _maintenanceService;
    private readonly Logger _logger;

    public int DefaultInterval => 1440; // 24 hours

    public DatabaseVacuumTask(IDatabaseMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        _logger.Info("Executing scheduled database maintenance task");
        var freelistCount = _maintenanceService.GetFreelistCount();

        if (freelistCount > 0 || !_maintenanceService.IsIncrementalAutoVacuumEnabled())
        {
            _logger.Info(
                "Freelist has {0} pages (auto-vacuum enabled: {1}); running maintenance",
                freelistCount,
                _maintenanceService.IsIncrementalAutoVacuumEnabled());
            _maintenanceService.PerformMaintenance(5000);
        }
        else
        {
            _logger.Debug("Freelist count is 0; skipping incremental vacuum");
        }
    }

    public void Execute(DatabaseVacuumCommand command)
    {
        var maxPages = command?.MaxPages ?? 5000;
        _logger.Info("Executing database vacuum via command queue (maxPages: {0})", maxPages);
        _maintenanceService.PerformMaintenance(maxPages);
    }
}
