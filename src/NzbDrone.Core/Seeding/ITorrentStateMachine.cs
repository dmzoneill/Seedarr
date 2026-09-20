using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public interface ITorrentStateMachine
{
    bool HandleForceCompleted(Torrent torrent);
    bool CheckDownloadThreshold(Torrent torrent, double defaultThreshold);
    List<Torrent> ApplyRatioLimit(List<Torrent> seedingTorrents, double globalRatioLimit, string action = "Stop");
    bool CanScheduleTransfers(Torrent torrent);
    void TransitionToChecking(Torrent torrent);
    TorrentStatus TransitionFromChecking(Torrent torrent);
}
