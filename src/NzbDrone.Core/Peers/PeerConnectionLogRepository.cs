using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Peers;

public interface IPeerConnectionLogRepository : IBasicRepository<PeerConnectionLog>
{
    List<PeerConnectionLog> GetByTimeRange(DateTime start, DateTime end, int limit = 1000, int offset = 0);
    List<PeerConnectionLog> GetByInfoHash(string infoHash, DateTime start, DateTime end, int limit = 1000, int offset = 0);
    List<PeerConnectionLog> GetLogs(DateTime start, DateTime end, int limit = 1000, int offset = 0);
    List<PeerConnectionLog> GetLogsByInfoHash(string infoHash, DateTime start, DateTime end, int limit = 1000, int offset = 0);
    (int EncryptedCount, int PlaintextCount) GetConnectionCounts(DateTime start, DateTime end);
    void Purge(DateTime before);
    void Purge(DateTime before, int maxLogs);
    void Purge(DateTime before, int maxLogs, int batchSize);
}

public class PeerConnectionLogRepository : BasicRepository<PeerConnectionLog>, IPeerConnectionLogRepository
{
    public PeerConnectionLogRepository(IDatabase database)
        : base(database)
    {
    }

    public List<PeerConnectionLog> GetByTimeRange(DateTime start, DateTime end, int limit = 1000, int offset = 0)
    {
        var clampedLimit = limit <= 0 ? 1000 : Math.Min(limit, 5000);
        var clampedOffset = Math.Max(0, offset);

        return QueryWithRetry(connection =>
            connection.Query<PeerConnectionLog>(
                $"SELECT * FROM \"{_table}\" WHERE \"Timestamp\" >= @Start AND \"Timestamp\" <= @End ORDER BY \"Timestamp\" DESC LIMIT @Limit OFFSET @Offset",
                new { Start = start, End = end, Limit = clampedLimit, Offset = clampedOffset }).ToList());
    }

    public List<PeerConnectionLog> GetByInfoHash(string infoHash, DateTime start, DateTime end, int limit = 1000, int offset = 0)
    {
        var clampedLimit = limit <= 0 ? 1000 : Math.Min(limit, 5000);
        var clampedOffset = Math.Max(0, offset);

        return QueryWithRetry(connection =>
            connection.Query<PeerConnectionLog>(
                $"SELECT * FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash AND \"Timestamp\" >= @Start AND \"Timestamp\" <= @End ORDER BY \"Timestamp\" DESC LIMIT @Limit OFFSET @Offset",
                new { InfoHash = infoHash, Start = start, End = end, Limit = clampedLimit, Offset = clampedOffset }).ToList());
    }

    public List<PeerConnectionLog> GetLogs(DateTime start, DateTime end, int limit = 1000, int offset = 0) =>
        GetByTimeRange(start, end, limit, offset);

    public List<PeerConnectionLog> GetLogsByInfoHash(string infoHash, DateTime start, DateTime end, int limit = 1000, int offset = 0) =>
        GetByInfoHash(infoHash, start, end, limit, offset);

    public (int EncryptedCount, int PlaintextCount) GetConnectionCounts(DateTime start, DateTime end)
    {
        return QueryWithRetry(connection =>
        {
            var isEncryptedValues = connection.Query<bool>(
                $"SELECT \"IsEncrypted\" FROM \"{_table}\" WHERE \"EventType\" = 'Connected' AND \"Timestamp\" >= @Start AND \"Timestamp\" <= @End",
                new { Start = start, End = end });

            var encrypted = 0;
            var plaintext = 0;
            foreach (var isEncrypted in isEncryptedValues)
            {
                if (isEncrypted)
                {
                    encrypted++;
                }
                else
                {
                    plaintext++;
                }
            }

            return (encrypted, plaintext);
        });
    }

    public void Purge(DateTime before)
    {
        Purge(before, 50000, 1000);
    }

    public void Purge(DateTime before, int maxLogs)
    {
        Purge(before, maxLogs, 1000);
    }

    public void Purge(DateTime before, int maxLogs, int batchSize)
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
                        WHERE ""Timestamp"" < @Before
                        LIMIT @BatchSize
                    )",
                    new { Before = before, BatchSize = clampedBatchSize });

                if (rowsAffected < clampedBatchSize)
                {
                    break;
                }

                Thread.Sleep(1);
            }

            if (maxLogs > 0)
            {
                while (true)
                {
                    var rowsAffected = connection.Execute(
                        $@"DELETE FROM ""{_table}""
                        WHERE ""Id"" IN (
                            SELECT ""Id"" FROM (
                                SELECT ""Id"", ROW_NUMBER() OVER (ORDER BY ""Timestamp"" DESC, ""Id"" DESC) AS rn
                                FROM ""{_table}""
                            ) sub
                            WHERE sub.rn > @MaxLogs
                            LIMIT @BatchSize
                        )",
                        new { MaxLogs = maxLogs, BatchSize = clampedBatchSize });

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
