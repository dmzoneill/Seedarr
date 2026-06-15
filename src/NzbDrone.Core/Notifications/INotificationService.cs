using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications;

public interface INotificationService : IProvider
{
    void OnTorrentAdded(string torrentName);
    void OnSeedingStarted(string torrentName);
    void OnSeedingStopped(string torrentName);
    void OnHealthIssue(string source, string message);
}
