using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.ArrIntegration;

public class SyncArrCommandExecutor : IExecute<SyncArrCommand>
{
    private readonly IArrSyncService _arrSyncService;
    private readonly Logger _logger;

    public SyncArrCommandExecutor(IArrSyncService arrSyncService)
    {
        _arrSyncService = arrSyncService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(SyncArrCommand command)
    {
        _logger.Info("Syncing all Arr connections via command");
        var result = _arrSyncService.Sync();
        _logger.Info("Arr sync complete: {0} added, {1} skipped, {2} failed", result.Added, result.Skipped, result.Failed);
    }
}
