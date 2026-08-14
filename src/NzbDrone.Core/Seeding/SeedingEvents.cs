using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Seeding;

public class SeedingTickEvent : IEvent
{
    public int ActiveTorrents { get; }

    public SeedingTickEvent(int activeTorrents)
    {
        ActiveTorrents = activeTorrents;
    }
}

public class SeedingStartedEvent : IEvent
{
    public int TorrentId { get; }

    public SeedingStartedEvent(int torrentId)
    {
        TorrentId = torrentId;
    }
}

public class SeedingStoppedEvent : IEvent
{
    public int TorrentId { get; }

    public SeedingStoppedEvent(int torrentId)
    {
        TorrentId = torrentId;
    }
}
