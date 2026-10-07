using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Developer;

public class DeveloperSyntheticEvent : IEvent
{
    public string Name { get; }

    public string Payload { get; }

    public DeveloperSyntheticEvent(string name, string payload)
    {
        Name = name;
        Payload = payload;
    }
}
