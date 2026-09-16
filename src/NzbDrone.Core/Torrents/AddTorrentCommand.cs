using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Torrents;

public class AddTorrentCommand : Command
{
    public string FilePath { get; set; }
    public double? Progress { get; set; }
    public bool? ForceCompleted { get; set; }
}
