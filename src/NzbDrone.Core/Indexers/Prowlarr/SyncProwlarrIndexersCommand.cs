using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Indexers.Prowlarr;

public class SyncProwlarrIndexersCommand : Command
{
    public int? ProwlarrIndexerId { get; set; }

    public SyncProwlarrIndexersCommand()
    {
    }

    public SyncProwlarrIndexersCommand(int? prowlarrIndexerId)
    {
        ProwlarrIndexerId = prowlarrIndexerId;
    }
}
