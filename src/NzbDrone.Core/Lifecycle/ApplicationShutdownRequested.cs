using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Lifecycle;

public class ApplicationShutdownRequested : IEvent
{
    public bool Restarting { get; set; }
}
