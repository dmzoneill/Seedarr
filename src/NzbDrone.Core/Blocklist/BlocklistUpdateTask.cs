using System;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Blocklist;

public class BlocklistUpdateTask : IScheduledTask, IExecute<BlocklistUpdateCommand>
{
    private readonly IPeerBlocklistSyncService _syncService;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public int DefaultInterval
    {
        get
        {
            if (_configService == null)
            {
                return 1440;
            }

            var days = _configService.BlocklistAutoUpdateIntervalDays > 0
                ? _configService.BlocklistAutoUpdateIntervalDays
                : _configService.BlocklistUpdateIntervalDays;

            return days > 0 ? days * 1440 : 1440;
        }
    }

    public BlocklistUpdateTask(
        IPeerBlocklistSyncService syncService,
        IConfigService configService,
        Logger logger = null)
    {
        _syncService = syncService;
        _configService = configService;
        _logger = logger ?? LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        if (_configService != null && !_configService.BlocklistEnabled)
        {
            _logger.Debug("Blocklist update skipped: blocklist is disabled");
            return;
        }

        _logger.Info("Executing scheduled blocklist update");
        try
        {
            _syncService.SyncAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Scheduled blocklist update failed");
        }
    }

    public void Execute(BlocklistUpdateCommand command)
    {
        var force = command?.Force ?? false;
        if (!force && _configService != null && !_configService.BlocklistEnabled)
        {
            _logger.Debug("Blocklist update command skipped: blocklist is disabled");
            return;
        }

        _logger.Info("Executing blocklist update via command queue (force: {0})", force);
        try
        {
            _syncService.SyncAsync(force: force).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Blocklist update command failed");
            throw;
        }
    }
}
