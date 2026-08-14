using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TrackerEntryRepository : BasicRepository<TrackerEntry>, ITrackerEntryRepository
{
    private readonly IDatabase _db;

    public TrackerEntryRepository(IDatabase database)
        : base(database)
    {
        _db = database;
    }

    public List<TrackerEntry> GetByTorrentId(int torrentId)
    {
        using var connection = _db.OpenConnection();
        return connection.Query<TrackerEntry>(
            $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Tier\", \"Url\"",
            new { TorrentId = torrentId }).ToList();
    }

    public void DeleteByTorrentId(int torrentId)
    {
        using var connection = _db.OpenConnection();
        connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId",
            new { TorrentId = torrentId });
    }
}
