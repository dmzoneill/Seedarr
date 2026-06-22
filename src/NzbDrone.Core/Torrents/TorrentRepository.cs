using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentRepository : BasicRepository<Torrent>, ITorrentRepository
{
    private readonly IDatabase _database;

    public TorrentRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public bool ExistsByInfoHash(string infoHash)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<int>(
            $"SELECT COUNT(1) FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash",
            new { InfoHash = infoHash }) > 0;
    }
}
