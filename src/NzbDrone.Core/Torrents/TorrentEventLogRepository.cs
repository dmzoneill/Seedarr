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
    void Purge(DateTime before, int maxLogsPerTorrent, int batchSize);
}

public class TorrentEventLogRepository : BasicRepository<TorrentEventLog>, ITorrentEventLogRepository
{
    public TorrentEventLogRepository(IDatabase database)
        : base(database)
    {
    }

    public List<TorrentEventLog> GetByTorrentId(int torrentId, int count)
    {
        return QueryWithRetry(connection =>
            connection.Query<TorrentEventLog>(
                $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"TimeStamp\" DESC LIMIT @Count",
                new { TorrentId = torrentId, Count = count }).ToList());
    }

    public void Purge(DateTime before)
    {
        Purge(before, 1000, 1000);
    }

    public void Purge(DateTime before, int maxLogsPerTorrent)
    {
        Purge(before, maxLogsPerTorrent, 1000);
    }

    public void Purge(DateTime before, int maxLogsPerTorrent, int batchSize)
    {
        var clampedBatchSize = batchSize <= 0 ? 1000 : batchSize;

        ExecuteWithRetry(connection =>
        {
            while (true)
            {
                var rowsAffected = connection.Execute(
                    $@"DELETE FROM ""{_table}""
                    WHERE ""Id"" IN (
                        SELECT ""Id"" FROM ""{_table}""
                        WHERE ""TimeStamp"" < @Before
                        LIMIT @BatchSize
                    )",
                    new { Before = before, BatchSize = clampedBatchSize });

                if (rowsAffected < clampedBatchSize)
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
                            LIMIT @BatchSize
                        )",
                        new { MaxLogs = maxLogsPerTorrent, BatchSize = clampedBatchSize });

                    if (rowsAffected < clampedBatchSize)
                    {
                        break;
                    }

                    Thread.Sleep(1);
                }
            }
        });
    }
}
