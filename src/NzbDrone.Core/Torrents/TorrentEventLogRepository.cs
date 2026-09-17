using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

            while (true)
            {
                var rowsAffected = connection.Execute(
                    $@"DELETE FROM ""{_table}""
                       WHERE ""Id"" IN (
                           SELECT ""Id"" FROM ""{_table}""
                           WHERE ""TimeStamp"" < @Before
                           LIMIT 500
                       )",
                    new { Before = before });

                if (rowsAffected == 0)
                {
                    break;
                }

                Thread.Sleep(1);
            }

            if (maxLogsPerTorrent > 0)
            {
                while (true)
                {
                    var rowsAffected = connection.Execute(
                        $@"DELETE FROM ""{_table}""
                           WHERE ""Id"" IN (
                               SELECT ""Id"" FROM (
                                   SELECT ""Id"", ROW_NUMBER() OVER (PARTITION BY ""TorrentId"" ORDER BY ""TimeStamp"" DESC, ""Id"" DESC) AS rn
                                   FROM ""{_table}""
                               ) sub
                               WHERE sub.rn > @MaxLogs
                               LIMIT 500
                           )",
                        new { MaxLogs = maxLogsPerTorrent });

                    if (rowsAffected == 0)
                    {
                        break;
                    }

                    Thread.Sleep(1);
                }
            }
        });
    }
}
