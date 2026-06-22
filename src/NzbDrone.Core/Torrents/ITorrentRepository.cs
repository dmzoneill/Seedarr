using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITorrentRepository : IBasicRepository<Torrent>
{
    bool ExistsByInfoHash(string infoHash);
}
