using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentFileRepository : BasicRepository<TorrentFile>, ITorrentFileRepository
{
    public TorrentFileRepository(IDatabase database)
        : base(database)
    {
    }

    public List<TorrentFile> GetByTorrentId(int torrentId)
    {
        return QueryWithRetry(connection =>
            connection.Query<TorrentFile>(
                $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Path\"",
                new { TorrentId = torrentId }).ToList());
    }

    public void DeleteByTorrentId(int torrentId)
    {
        ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId }));
    }
}
