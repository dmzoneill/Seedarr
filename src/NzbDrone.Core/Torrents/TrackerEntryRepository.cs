using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TrackerEntryRepository : BasicRepository<TrackerEntry>, ITrackerEntryRepository
{
    public TrackerEntryRepository(IDatabase database)
        : base(database)
    {
    }

    public List<TrackerEntry> GetByTorrentId(int torrentId)
    {
        return QueryWithRetry(connection =>
            connection.Query<TrackerEntry>(
                $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Tier\", \"Url\"",
                new { TorrentId = torrentId }).ToList());
    }

    public void DeleteByTorrentId(int torrentId)
    {
        ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId }));
    }
}
