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
    public Torrent Torrent { get; }

    public TorrentDeletedEvent(int torrentId, Torrent torrent = null)
    {
        TorrentId = torrentId;
        Torrent = torrent;
    }
}

public class TorrentDownloadCompletedEvent : IEvent
{
    public Torrent Torrent { get; }

    public TorrentDownloadCompletedEvent(Torrent torrent)
    {
        Torrent = torrent;
    }
}

public class TorrentSeedGoalReachedEvent : IEvent
{
    public Torrent Torrent { get; }

    public TorrentSeedGoalReachedEvent(Torrent torrent)
    {
        Torrent = torrent;
    }
}
