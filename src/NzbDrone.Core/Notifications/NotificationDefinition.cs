using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications;

public class NotificationDefinition : ProviderDefinition
{
    public bool OnTorrentAdded { get; set; }
    public bool OnSeedingStarted { get; set; }
    public bool OnSeedingStopped { get; set; }
    public bool OnHealthIssue { get; set; }
}
