using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentFileRepository : BasicRepository<TorrentFile>, ITorrentFileRepository
{
    private readonly IDatabase _db;

    public TorrentFileRepository(IDatabase database)
        : base(database)
    {
        _db = database;
    }

    public List<TorrentFile> GetByTorrentId(int torrentId)
    {
        using var connection = _db.OpenConnection();
        return connection.Query<TorrentFile>(
            $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Path\"",
            new { TorrentId = torrentId }).ToList();
    }

    public void DeleteByTorrentId(int torrentId)
    {
        using var connection = _db.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId",
            new { TorrentId = torrentId });
    }
}
