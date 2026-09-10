using System.Collections.Generic;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Seeding;

public interface IStopPolicy
{
    HashSet<int> SelectStoppedTorrents(List<Torrent> torrents);
    HashSet<int> SelectDownloadStoppedTorrents(List<Torrent> torrents);
    HashSet<int> SelectStoppedTorrents(List<Torrent> torrents, double minPct, double maxPct);
}
