using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Peers;

public interface IPeerConnectionLogRepository : IBasicRepository<PeerConnectionLog>
{
    List<PeerConnectionLog> GetByTimeRange(DateTime start, DateTime end);
    List<PeerConnectionLog> GetByInfoHash(string infoHash, DateTime start, DateTime end);
    void Purge(DateTime before);
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

    public void Purge(DateTime before)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"Timestamp\" < @Before",
            new { Before = before });
    }
}
