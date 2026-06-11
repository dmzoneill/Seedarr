using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentRepository : BasicRepository<Torrent>, ITorrentRepository
{
    public TorrentRepository(IDatabase database)
        : base(database)
    {
    }
}
