using System.Collections.Generic;
using System.Linq;
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
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return false;
        }

        return RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            return connection.QueryFirstOrDefault<int>(
                $"SELECT COUNT(1) FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash",
                new { InfoHash = infoHash }) > 0;
        });
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<Torrent>(
            $"SELECT * FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash",
            new { InfoHash = infoHash });
    }

    public Torrent FindByInfoHash(string infoHash)
    {
        return GetByInfoHash(infoHash);
    }

    public List<Torrent> GetByInfoHashes(IEnumerable<string> infoHashes)
    {
        if (infoHashes == null)
        {
            return new List<Torrent>();
        }

        var hashes = infoHashes.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToList();
        if (hashes.Count == 0)
        {
            return new List<Torrent>();
        }

        using var connection = _database.OpenConnection();
        var result = new List<Torrent>();

        foreach (var batch in hashes.Chunk(500))
        {
            var records = connection.Query<Torrent>(
                $"SELECT * FROM \"{_table}\" WHERE \"InfoHash\" IN @Hashes",
                new { Hashes = batch });

            result.AddRange(records);
        }

        return result;
    }

    public int GetNextSortOrder()
    {
        return RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            return connection.QueryFirstOrDefault<int>(
                $"SELECT COALESCE(MAX(\"SortOrder\"), -1) + 1 FROM \"{_table}\"");
        });
    }

    public void UpdateCategoryName(string oldCategoryName, string newCategoryName)
    {
        if (string.IsNullOrWhiteSpace(oldCategoryName) || string.IsNullOrWhiteSpace(newCategoryName))
        {
            return;
        }

        RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            connection.Execute(
                $"UPDATE \"{_table}\" SET \"Category\" = @NewName WHERE LOWER(\"Category\") = LOWER(@OldName)",
                new { OldName = oldCategoryName.Trim(), NewName = newCategoryName.Trim() });
        });
    }

    public void ClearCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return;
        }

        RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            connection.Execute(
                $"UPDATE \"{_table}\" SET \"Category\" = '' WHERE LOWER(\"Category\") = LOWER(@CategoryName)",
                new { CategoryName = categoryName.Trim() });
        });
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
