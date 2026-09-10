namespace NzbDrone.Core.Torrents;

public enum TorrentStatus
{
    Stopped = 0,
    Seeding = 1,
    Paused = 2,
    Error = 3,
    Queued = 4,
    Downloading = 5
}
