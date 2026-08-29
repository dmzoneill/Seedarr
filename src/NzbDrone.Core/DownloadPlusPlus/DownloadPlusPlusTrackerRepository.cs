using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.DownloadPlusPlus;

public interface IDownloadPlusPlusTrackerRepository : IBasicRepository<DownloadPlusPlusTracker>
{
    DownloadPlusPlusTracker FindByUrl(string url);
    List<DownloadPlusPlusTracker> GetAliveTrackers();
    List<DownloadPlusPlusTracker> GetBySource(TrackerSourceType source);
}

public class DownloadPlusPlusTrackerRepository : BasicRepository<DownloadPlusPlusTracker>, IDownloadPlusPlusTrackerRepository
{
    private readonly IDatabase _database;

    public DownloadPlusPlusTrackerRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public DownloadPlusPlusTracker FindByUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<DownloadPlusPlusTracker>(
            $"SELECT * FROM \"{_table}\" WHERE LOWER(\"Url\") = LOWER(@Url)",
            new { Url = url.Trim() });
    }

    public List<DownloadPlusPlusTracker> GetAliveTrackers()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<DownloadPlusPlusTracker>(
            $"SELECT * FROM \"{_table}\" WHERE \"Enabled\" = 1 AND (\"Status\" = 1 OR \"Status\" = 2) ORDER BY \"LatencyMs\" ASC")
            .ToList();
    }

    public List<DownloadPlusPlusTracker> GetBySource(TrackerSourceType source)
    {
        using var connection = _database.OpenConnection();
        return connection.Query<DownloadPlusPlusTracker>(
            $"SELECT * FROM \"{_table}\" WHERE \"Source\" = @Source ORDER BY \"Id\" DESC",
            new { Source = (int)source })
            .ToList();
    }
}
