using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers.Metrics;

public interface ITrackerMetricSnapshotRepository : IBasicRepository<TrackerMetricSnapshot>
{
    List<TrackerMetricSnapshot> GetHistory(int trackerMetricId, DateTime since, int limit = 500);
    List<TrackerMetricSnapshot> GetRecentSnapshots(DateTime since);
    List<HourlyTrackerMetricPoint> GetHourlyAggregatedMetrics(DateTime since);
    void PruneOlderThan(DateTime cutoff);
    void DeleteByMetricId(int trackerMetricId);
}

public class TrackerMetricSnapshotRepository : BasicRepository<TrackerMetricSnapshot>, ITrackerMetricSnapshotRepository
{
    private readonly IDatabase _database;

    public TrackerMetricSnapshotRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public List<TrackerMetricSnapshot> GetHistory(int trackerMetricId, DateTime since, int limit = 500)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<TrackerMetricSnapshot>(
            $"SELECT * FROM \"{_table}\" WHERE \"TrackerMetricId\" = @MetricId AND \"Timestamp\" >= @Since ORDER BY \"Timestamp\" ASC LIMIT @Limit",
            new { MetricId = trackerMetricId, Since = since, Limit = limit })
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

    public List<HourlyTrackerMetricPoint> GetHourlyAggregatedMetrics(DateTime since)
    {
        using var connection = _database.OpenConnection();
        var bucketExpr = _database.DatabaseType == DatabaseType.PostgreSQL
            ? "to_char(\"Timestamp\", 'YYYY-MM-DD HH24:00:00')"
            : "strftime('%Y-%m-%d %H:00:00', \"Timestamp\")";

        var sql = $@"
            SELECT
                {bucketExpr} AS ""Bucket"",
                COALESCE(SUM(""Uploaded""), 0) AS ""Uploaded"",
                COALESCE(SUM(""Downloaded""), 0) AS ""Downloaded"",
                COUNT(CASE WHEN ""Operation"" = 'Announce' THEN 1 END) AS ""Announces"",
                COALESCE(SUM(""PeersDiscovered""), 0) AS ""PeersDiscovered"",
                COALESCE(AVG(CASE WHEN ""ResponseTimeMs"" > 0 THEN ""ResponseTimeMs"" END), 0.0) AS ""AvgLatencyMs""
            FROM ""{_table}""
            WHERE ""Timestamp"" >= @Since
            GROUP BY {bucketExpr}
            ORDER BY {bucketExpr} ASC";

        return connection.Query<HourlyTrackerMetricPoint>(sql, new { Since = since }).ToList();
    }

    public void PruneOlderThan(DateTime cutoff)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"Timestamp\" < @Cutoff",
            new { Cutoff = cutoff });
    }

    public void DeleteByMetricId(int trackerMetricId)
    {
        using var connection = _database.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"TrackerMetricId\" = @MetricId",
            new { MetricId = trackerMetricId });
    }
}
