using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITorrentRepository : IBasicRepository<Torrent>
{
    bool ExistsByInfoHash(string infoHash);
    Torrent GetByInfoHash(string infoHash);
    List<Torrent> GetByInfoHashes(IEnumerable<string> infoHashes);
    void UpdateCategoryName(string oldCategoryName, string newCategoryName);
    void ClearCategory(string categoryName);
}
