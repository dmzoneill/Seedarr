using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public interface ITorrentStateMachine
{
    bool HandleForceCompleted(Torrent torrent);
    bool CheckDownloadThreshold(Torrent torrent, double defaultThreshold);
    void ApplyRatioLimit(List<Torrent> seedingTorrents, double globalRatioLimit);
}
