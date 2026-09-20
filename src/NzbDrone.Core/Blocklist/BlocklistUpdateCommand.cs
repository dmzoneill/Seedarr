using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Blocklist;

public class BlocklistUpdateCommand : Command
{
    public bool Force { get; set; }
}
