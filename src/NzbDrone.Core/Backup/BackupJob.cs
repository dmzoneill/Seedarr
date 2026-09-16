using NLog;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Backup;

public class BackupJob : IScheduledTask, IExecute<BackupCommand>
{
    private readonly IBackupService _backupService;
    private readonly Logger _logger;

    public int DefaultInterval => 1440; // 24 hours

    public BackupJob(IBackupService backupService)
    {
        _backupService = backupService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        _logger.Info("Executing automated scheduled backup");
        _backupService.CreateBackup(BackupType.Scheduled);
    }

    public void Execute(BackupCommand command)
    {
        var backupType = command?.Type ?? BackupType.Manual;
        _logger.Info("Executing backup via command queue (type: {0})", backupType);
        _backupService.CreateBackup(backupType);
    }
}
