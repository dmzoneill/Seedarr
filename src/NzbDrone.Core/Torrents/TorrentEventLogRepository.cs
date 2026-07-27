using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public interface ITorrentEventLogRepository : IBasicRepository<TorrentEventLog>
{
    List<TorrentEventLog> GetByTorrentId(int torrentId, int count);
    void Purge(DateTime before);
}

public class TorrentEventLogRepository : BasicRepository<TorrentEventLog>, ITorrentEventLogRepository
{
    private readonly IDatabase _database;

    public TorrentEventLogRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public List<TorrentEventLog> GetByTorrentId(int torrentId, int count)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<TorrentEventLog>(
            $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"TimeStamp\" DESC LIMIT @Count",
            new { TorrentId = torrentId, Count = count }).ToList();
    }

    public void Purge(DateTime before)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"TimeStamp\" < @Before",
            new { Before = before });
    }
}
