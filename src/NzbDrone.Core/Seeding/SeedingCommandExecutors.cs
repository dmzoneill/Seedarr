using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Seeding;

public class StartSeedingCommandExecutor : IExecute<StartSeedingCommand>
{
    private readonly ISeedingService _seedingService;
    private readonly Logger _logger;

    public StartSeedingCommandExecutor(ISeedingService seedingService)
    {
        _seedingService = seedingService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(StartSeedingCommand command)
    {
        _logger.Info("Starting seeding for torrent {0} via command", command.TorrentId);
        _seedingService.Start(command.TorrentId);
    }
}

public class StopSeedingCommandExecutor : IExecute<StopSeedingCommand>
{
    private readonly ISeedingService _seedingService;
    private readonly Logger _logger;

    public StopSeedingCommandExecutor(ISeedingService seedingService)
    {
        _seedingService = seedingService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(StopSeedingCommand command)
    {
        _logger.Info("Stopping seeding for torrent {0} via command", command.TorrentId);
        _seedingService.Stop(command.TorrentId);
    }
}

public class StartAllSeedingCommandExecutor : IExecute<StartAllSeedingCommand>
{
    private readonly ISeedingService _seedingService;
    private readonly Logger _logger;

    public StartAllSeedingCommandExecutor(ISeedingService seedingService)
    {
        _seedingService = seedingService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(StartAllSeedingCommand command)
    {
        _logger.Info("Starting all seeding via command");
        _seedingService.StartAll();
    }
}

public class StopAllSeedingCommandExecutor : IExecute<StopAllSeedingCommand>
{
    private readonly ISeedingService _seedingService;
    private readonly Logger _logger;

    public StopAllSeedingCommandExecutor(ISeedingService seedingService)
    {
        _seedingService = seedingService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute(StopAllSeedingCommand command)
    {
        _logger.Info("Stopping all seeding via command");
        _seedingService.StopAll();
    }
}
