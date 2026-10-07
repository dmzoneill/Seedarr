using System;
using System.Threading;
using NLog;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Datastore;

public class WalCheckpointTask : IScheduledTask, IExecute<WalCheckpointCommand>
{
    private readonly IDatabaseMaintenanceService _maintenanceService;
    private readonly Logger _logger;

    public int DefaultInterval => 15; // 15 minutes

    public WalCheckpointTask(IDatabaseMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.Info("Executing scheduled WAL checkpoint task");
        var result = _maintenanceService.CheckpointWal(WalCheckpointMode.Passive);
        cancellationToken.ThrowIfCancellationRequested();

        if (result != null
            && result.Success
            && result.Busy == 0
            && string.Equals(result.DatabaseType, DatabaseType.SQLite.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.Debug("Passive WAL checkpoint succeeded with no busy readers; executing RESTART checkpoint to reset WAL write pointer");
            cancellationToken.ThrowIfCancellationRequested();
            _maintenanceService.CheckpointWal(WalCheckpointMode.Restart);
        }
    }

    public void Execute(WalCheckpointCommand command)
    {
        var mode = command?.Mode ?? WalCheckpointMode.Passive;
        _logger.Info("Executing WAL checkpoint via command queue (Mode: {0})", mode);
        _maintenanceService.CheckpointWal(mode);
    }
}
