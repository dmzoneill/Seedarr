using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class TorrentRepository : BasicRepository<Torrent>, ITorrentRepository
{
    public TorrentRepository(IDatabase database)
        : base(database)
    {
    }

    public bool ExistsByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return false;
        }

        var normalized = infoHash.Trim().ToLowerInvariant();

        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<int>(
                $"SELECT COUNT(1) FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash COLLATE NOCASE",
                new { InfoHash = normalized }) > 0);
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var normalized = infoHash.Trim().ToLowerInvariant();

        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<Torrent>(
                $"SELECT * FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash COLLATE NOCASE",
                new { InfoHash = normalized }));
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

        var hashes = infoHashes.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim().ToLowerInvariant()).Distinct().ToList();
        if (hashes.Count == 0)
        {
            return new List<Torrent>();
        }

        return QueryWithRetry(connection =>
        {
            var result = new List<Torrent>();

            foreach (var batch in hashes.Chunk(500))
            {
                var records = connection.Query<Torrent>(
                    $"SELECT * FROM \"{_table}\" WHERE \"InfoHash\" COLLATE NOCASE IN @Hashes",
                    new { Hashes = batch });

                result.AddRange(records);
            }

            return result;
        });
    }

    public int GetNextSortOrder()
    {
        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<int>(
                $"SELECT COALESCE(MAX(\"SortOrder\"), -1) + 1 FROM \"{_table}\""));
    }

    public void UpdateCategoryName(string oldCategoryName, string newCategoryName)
    {
        if (string.IsNullOrWhiteSpace(oldCategoryName) || string.IsNullOrWhiteSpace(newCategoryName))
        {
            return;
        }

        ExecuteWithRetry(connection =>
            connection.Execute(
                $"UPDATE \"{_table}\" SET \"Category\" = @NewName WHERE LOWER(\"Category\") = LOWER(@OldName)",
                new { OldName = oldCategoryName.Trim(), NewName = newCategoryName.Trim() }));
    }

    public void ClearCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return;
        }

        ExecuteWithRetry(connection =>
            connection.Execute(
                $"UPDATE \"{_table}\" SET \"Category\" = '' WHERE LOWER(\"Category\") = LOWER(@CategoryName)",
                new { CategoryName = categoryName.Trim() }));
    }

    public void UpdateTagsAndLabels(IEnumerable<Torrent> torrents)
    {
        var list = torrents?.ToList();
        if (list == null || list.Count == 0)
        {
            return;
        }

        ExecuteWithRetry(connection =>
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var torrent in list)
                {
                    var tagIdsJson = JsonSerializer.Serialize(torrent.TagIds ?? new List<int>());
                    connection.Execute(
                        $"UPDATE \"{_table}\" SET \"TagIds\" = @TagIds, \"Label\" = @Label WHERE \"Id\" = @Id",
                        new { Id = torrent.Id, TagIds = tagIdsJson, Label = torrent.Label ?? string.Empty },
                        transaction);
                }

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

    public void UpdateTagsAndLabel(int id, List<int> tagIds, string label)
    {
        UpdateTagsAndLabels(new[] { new Torrent { Id = id, TagIds = tagIds, Label = label } });
    }

    public void DeleteMany(List<int> torrentIds, bool deleteFiles = false)
    {
        if (torrentIds == null || torrentIds.Count == 0)
        {
            return;
        }

        var ids = torrentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        ExecuteWithRetry(connection =>
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var batch in ids.Chunk(500))
                {
                    var infoHashes = connection.Query<string>(
                        $"SELECT \"InfoHash\" FROM \"{_table}\" WHERE \"Id\" IN @Ids AND \"InfoHash\" IS NOT NULL",
                        new { Ids = batch },
                        transaction)
                        .Where(h => !string.IsNullOrWhiteSpace(h))
                        .Distinct()
                        .ToList();

                    if (infoHashes.Count > 0)
                    {
                        foreach (var hashBatch in infoHashes.Chunk(500))
                        {
                            connection.Execute(
                                "DELETE FROM \"PeerConnectionLogs\" WHERE \"InfoHash\" IN @Hashes",
                                new { Hashes = hashBatch },
                                transaction);
                        }
                    }

                    connection.Execute(
                        "DELETE FROM \"TorrentMediaMetadata\" WHERE \"TorrentId\" IN @Ids",
                        new { Ids = batch },
                        transaction);

                    connection.Execute(
                        "DELETE FROM \"TorrentFiles\" WHERE \"TorrentId\" IN @Ids",
                        new { Ids = batch },
                        transaction);

                    connection.Execute(
                        "DELETE FROM \"TrackerEntries\" WHERE \"TorrentId\" IN @Ids",
                        new { Ids = batch },
                        transaction);

                    connection.Execute(
                        "DELETE FROM \"TorrentEventLogs\" WHERE \"TorrentId\" IN @Ids",
                        new { Ids = batch },
                        transaction);

                    connection.Execute(
                        $"DELETE FROM \"{_table}\" WHERE \"Id\" IN @Ids",
                        new { Ids = batch },
                        transaction);
                }

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

    public override void Delete(int id)
    {
        DeleteMany(new List<int> { id });
    }
}
