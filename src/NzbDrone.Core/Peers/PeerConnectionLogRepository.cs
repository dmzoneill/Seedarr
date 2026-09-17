using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Peers;

public interface IPeerConnectionLogRepository : IBasicRepository<PeerConnectionLog>
{
    List<PeerConnectionLog> GetByTimeRange(DateTime start, DateTime end);
    List<PeerConnectionLog> GetByInfoHash(string infoHash, DateTime start, DateTime end);
    (int EncryptedCount, int PlaintextCount) GetConnectionCounts(DateTime start, DateTime end);
    void Purge(DateTime before);
    void Purge(DateTime before, int maxLogs);
}

public class PeerConnectionLogRepository : BasicRepository<PeerConnectionLog>, IPeerConnectionLogRepository
{
    private readonly IDatabase _database;

    public PeerConnectionLogRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public List<PeerConnectionLog> GetByTimeRange(DateTime start, DateTime end)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<PeerConnectionLog>(
            $"SELECT * FROM \"{_table}\" WHERE \"Timestamp\" >= @Start AND \"Timestamp\" <= @End ORDER BY \"Timestamp\" DESC",
            new { Start = start, End = end }).ToList();
    }

    public List<PeerConnectionLog> GetByInfoHash(string infoHash, DateTime start, DateTime end)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<PeerConnectionLog>(
            $"SELECT * FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash AND \"Timestamp\" >= @Start AND \"Timestamp\" <= @End ORDER BY \"Timestamp\" DESC",
            new { InfoHash = infoHash, Start = start, End = end }).ToList();
    }

    public (int EncryptedCount, int PlaintextCount) GetConnectionCounts(DateTime start, DateTime end)
    {
        using var connection = _database.OpenConnection();
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
    }

    public void Purge(DateTime before)
    {
        Purge(before, 50000);
    }

    public void Purge(DateTime before, int maxLogs)
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
                           WHERE ""Timestamp"" < @Before
                           LIMIT 500
                       )",
                    new { Before = before });

                if (rowsAffected == 0)
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
                               LIMIT 500
                           )",
                        new { MaxLogs = maxLogs });

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
