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

    public override void Delete(int id)
    {
        RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                var torrent = connection.QueryFirstOrDefault<Torrent>(
                    $"SELECT * FROM \"{_table}\" WHERE \"Id\" = @Id",
                    new { Id = id },
                    transaction);

                if (torrent != null && !string.IsNullOrWhiteSpace(torrent.InfoHash))
                {
                    connection.Execute(
                        "DELETE FROM \"PeerConnectionLogs\" WHERE \"InfoHash\" = @InfoHash",
                        new { torrent.InfoHash },
                        transaction);
                }

                connection.Execute(
                    "DELETE FROM \"TorrentFiles\" WHERE \"TorrentId\" = @Id",
                    new { Id = id },
                    transaction);

                connection.Execute(
                    "DELETE FROM \"TrackerEntries\" WHERE \"TorrentId\" = @Id",
                    new { Id = id },
                    transaction);

                connection.Execute(
                    "DELETE FROM \"TorrentEventLogs\" WHERE \"TorrentId\" = @Id",
                    new { Id = id },
                    transaction);

                connection.Execute(
                    $"DELETE FROM \"{_table}\" WHERE \"Id\" = @Id",
                    new { Id = id },
                    transaction);

                transaction.Commit();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // best-effort rollback
                }

                throw;
            }
        });
    }
}
