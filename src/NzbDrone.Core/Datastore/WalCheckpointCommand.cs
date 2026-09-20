using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Datastore;

public class WalCheckpointCommand : Command
{
    public WalCheckpointMode Mode { get; set; } = WalCheckpointMode.Passive;
}
