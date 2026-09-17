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
    void Purge(DateTime before, int maxLogsPerTorrent);
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
        Purge(before, 1000);
    }

    public void Purge(DateTime before, int maxLogsPerTorrent)
    {
        RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                connection.Execute(
                    $"DELETE FROM \"{_table}\" WHERE \"TimeStamp\" < @Before",
                    new { Before = before },
                    transaction);

                if (maxLogsPerTorrent > 0)
                {
                    connection.Execute(
                        $@"DELETE FROM ""{_table}""
                           WHERE ""Id"" IN (
                               SELECT ""Id"" FROM (
                                   SELECT ""Id"", ROW_NUMBER() OVER (PARTITION BY ""TorrentId"" ORDER BY ""TimeStamp"" DESC, ""Id"" DESC) AS rn
                                   FROM ""{_table}""
                               ) sub
                               WHERE sub.rn > @MaxLogs
                           )",
                        new { MaxLogs = maxLogsPerTorrent },
                        transaction);
                }

                transaction.Commit();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // best-effort rollback
                }

                throw;
            }
        });
    }
}
