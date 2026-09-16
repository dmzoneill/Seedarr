using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface IDownloadHistoryRepository : IBasicRepository<DownloadHistory>
{
    DownloadHistory FindByInfoHash(string infoHash);
    DownloadHistory FindByTorrentId(int torrentId);
    Dictionary<string, DownloadHistory> GetLatestByInfoHashes(IEnumerable<string> infoHashes);
    List<DownloadHistory> GetHistory(string query = null, string status = null, int limit = 500, int offset = 0);
    int DeleteOlderThan(global::System.DateTime cutoffDate);
    void DeleteAll();
}
