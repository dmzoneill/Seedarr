using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Backup;

public class BackupCommand : Command
{
    public BackupType Type { get; set; } = BackupType.Manual;
}
