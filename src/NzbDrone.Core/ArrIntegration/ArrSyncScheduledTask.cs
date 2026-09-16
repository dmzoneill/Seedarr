using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.ArrIntegration;

public class ArrSyncScheduledTask : IScheduledTask
{
    private readonly IArrConnectionFactory _connectionFactory;
    private readonly IArrSyncService _arrSyncService;
    private readonly Logger _logger;

    public int DefaultInterval => 60;

    public ArrSyncScheduledTask(
        IArrConnectionFactory connectionFactory,
        IArrSyncService arrSyncService)
    {
        _connectionFactory = connectionFactory;
        _arrSyncService = arrSyncService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        var connections = (_connectionFactory.All() ?? Enumerable.Empty<ArrConnectionDefinition>())
            .Where(c => c.Enable && c.SyncEnabled)
            .ToList();

        if (connections.Count == 0)
        {
            _logger.Debug("No enabled Arr connections configured for sync");
            return;
        }

        _logger.Info("Starting background Arr sync for {0} connection(s)", connections.Count);

        try
        {
            var result = _arrSyncService.Sync();
            if (result != null)
            {
                _logger.Info(
                    "Background Arr sync completed: {0} added, {1} skipped, {2} failed",
                    result.Added,
                    result.Skipped,
                    result.Failed);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Background Arr sync failed");
        }
    }
}
