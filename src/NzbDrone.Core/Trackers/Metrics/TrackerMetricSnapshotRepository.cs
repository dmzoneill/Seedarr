using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers.Metrics;

public interface ITrackerMetricSnapshotRepository : IBasicRepository<TrackerMetricSnapshot>
{
    List<TrackerMetricSnapshot> GetHistory(int trackerMetricId, DateTime since);
    List<TrackerMetricSnapshot> GetRecentSnapshots(DateTime since);
    void PruneOlderThan(DateTime cutoff);
}

public class TrackerMetricSnapshotRepository : BasicRepository<TrackerMetricSnapshot>, ITrackerMetricSnapshotRepository
{
    private readonly IDatabase _database;

    public TrackerMetricSnapshotRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public List<TrackerMetricSnapshot> GetHistory(int trackerMetricId, DateTime since)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<TrackerMetricSnapshot>(
            $"SELECT * FROM \"{_table}\" WHERE \"TrackerMetricId\" = @MetricId AND \"Timestamp\" >= @Since ORDER BY \"Timestamp\" ASC",
            new { MetricId = trackerMetricId, Since = since })
            .ToList();
    }

    public List<TrackerMetricSnapshot> GetRecentSnapshots(DateTime since)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<TrackerMetricSnapshot>(
            $"SELECT * FROM \"{_table}\" WHERE \"Timestamp\" >= @Since ORDER BY \"Timestamp\" ASC",
            new { Since = since })
            .ToList();
    }

    public void PruneOlderThan(DateTime cutoff)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"Timestamp\" < @Cutoff",
            new { Cutoff = cutoff });
    }
}
