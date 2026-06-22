using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITorrentFileRepository : IBasicRepository<TorrentFile>
{
    List<TorrentFile> GetByTorrentId(int torrentId);
    void DeleteByTorrentId(int torrentId);
}
