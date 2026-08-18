using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.TrackerBoost;

public interface ITrackerBoostTrackerRepository : IBasicRepository<TrackerBoostTracker>
{
    TrackerBoostTracker FindByUrl(string url);
    List<TrackerBoostTracker> GetAliveTrackers();
    List<TrackerBoostTracker> GetBySource(TrackerSourceType source);
}

public class TrackerBoostTrackerRepository : BasicRepository<TrackerBoostTracker>, ITrackerBoostTrackerRepository
{
    private readonly IDatabase _database;

    public TrackerBoostTrackerRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public TrackerBoostTracker FindByUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<TrackerBoostTracker>(
            $"SELECT * FROM \"{_table}\" WHERE LOWER(\"Url\") = LOWER(@Url)",
            new { Url = url.Trim() });
    }

    public List<TrackerBoostTracker> GetAliveTrackers()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<TrackerBoostTracker>(
            $"SELECT * FROM \"{_table}\" WHERE \"Enabled\" = 1 AND (\"Status\" = 1 OR \"Status\" = 2) ORDER BY \"LatencyMs\" ASC")
            .ToList();
    }

    public List<TrackerBoostTracker> GetBySource(TrackerSourceType source)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<TrackerBoostTracker>(
            $"SELECT * FROM \"{_table}\" WHERE \"Source\" = @Source ORDER BY \"Id\" DESC",
            new { Source = (int)source })
            .ToList();
    }
}
