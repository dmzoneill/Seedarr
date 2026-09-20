using System;
using NLog;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Indexers.Prowlarr;

public class ProwlarrIndexerSyncScheduledTask : IScheduledTask, IExecute<SyncProwlarrIndexersCommand>, IHandle<ApplicationStartedEvent>
{
    private readonly IProwlarrIndexerSyncService _syncService;
    private readonly Logger _logger;

    public int DefaultInterval => 360; // 6 hours (in minutes)

    public ProwlarrIndexerSyncScheduledTask(IProwlarrIndexerSyncService syncService)
    {
        _syncService = syncService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        _logger.Info("Starting scheduled Prowlarr indexer synchronization");
        try
        {
            var result = _syncService.Sync();
            if (result != null)
            {
                _logger.Info(
                    "Scheduled Prowlarr sync finished: {0} added, {1} updated, {2} removed",
                    result.Added,
                    result.Updated,
                    result.Removed);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Scheduled Prowlarr sync failed");
        }
    }

    public void Execute(SyncProwlarrIndexersCommand command)
    {
        _logger.Info("Executing Prowlarr indexer sync via command");
        var result = _syncService.Sync(command?.ProwlarrIndexerId);
        if (result != null)
        {
            _logger.Info(
                "Command Prowlarr sync finished: {0} added, {1} updated, {2} removed",
                result.Added,
                result.Updated,
                result.Removed);
        }
    }

    public void Handle(ApplicationStartedEvent message)
    {
        _logger.Debug("ProwlarrIndexerSyncScheduledTask handling ApplicationStartedEvent");
        try
        {
            _syncService.Sync();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Startup Prowlarr indexer sync encountered an issue");
        }
    }
}
