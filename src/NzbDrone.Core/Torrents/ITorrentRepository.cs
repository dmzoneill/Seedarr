using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITorrentRepository : IBasicRepository<Torrent>
{
    bool ExistsByInfoHash(string infoHash);
    Torrent GetByInfoHash(string infoHash);
    Torrent FindByInfoHash(string infoHash);
    List<Torrent> GetByInfoHashes(IEnumerable<string> infoHashes);
    int GetNextSortOrder();
    void UpdateCategoryName(string oldCategoryName, string newCategoryName);
    void ClearCategory(string categoryName);
}
