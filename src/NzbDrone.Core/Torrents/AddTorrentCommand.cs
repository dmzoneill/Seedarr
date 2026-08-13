using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Torrents;

public class AddTorrentCommand : Command
{
    public string FilePath { get; set; }
}
