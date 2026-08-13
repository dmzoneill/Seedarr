using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Torrents;

public class TorrentAddedEvent : IEvent
{
    public Torrent Torrent { get; }

    public TorrentAddedEvent(Torrent torrent)
    {
        Torrent = torrent;
    }
}

public class TorrentDeletedEvent : IEvent
{
    public int TorrentId { get; }

    public TorrentDeletedEvent(int torrentId)
    {
        TorrentId = torrentId;
    }
}
