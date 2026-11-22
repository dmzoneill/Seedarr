using System;
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
        {
            // Separate predicates so SQLite can use IX_Torrents_InfoHash / IX_Torrents_InfoHashV2
            // (expression indexes on LOWER(column)). An OR across the two columns does not.
            var v1 = connection.QueryFirstOrDefault<int>(
                $"SELECT COUNT(1) FROM \"{_table}\" WHERE LOWER(\"InfoHash\") = @InfoHash",
                new { InfoHash = normalized });
            if (v1 > 0)
            {
                return true;
            }

            var v2 = connection.QueryFirstOrDefault<int>(
                $"SELECT COUNT(1) FROM \"{_table}\" WHERE LOWER(\"InfoHashV2\") = @InfoHash",
                new { InfoHash = normalized });
            return v2 > 0;
        });
    }

    public Torrent GetByInfoHash(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        var normalized = infoHash.Trim().ToLowerInvariant();

        return QueryWithRetry(connection =>
        {
            var byV1 = connection.QueryFirstOrDefault<Torrent>(
                $"SELECT * FROM \"{_table}\" WHERE LOWER(\"InfoHash\") = @InfoHash",
                new { InfoHash = normalized });
            if (byV1 != null)
            {
                return byV1;
            }

            return connection.QueryFirstOrDefault<Torrent>(
                $"SELECT * FROM \"{_table}\" WHERE LOWER(\"InfoHashV2\") = @InfoHash",
                new { InfoHash = normalized });
        });
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
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var batch in hashes.Chunk(500))
            {
                var byV1 = connection.Query<Torrent>(
                    $"SELECT * FROM \"{_table}\" WHERE LOWER(\"InfoHash\") IN @Hashes",
                    new { Hashes = batch });
                var byV2 = connection.Query<Torrent>(
                    $"SELECT * FROM \"{_table}\" WHERE LOWER(\"InfoHashV2\") IN @Hashes",
                    new { Hashes = batch });

                foreach (var torrent in byV1.Concat(byV2))
                {
                    if (torrent == null)
                    {
                        continue;
                    }

                    var key = !string.IsNullOrWhiteSpace(torrent.InfoHash)
                        ? torrent.InfoHash
                        : torrent.InfoHashV2 ?? string.Empty;
                    if (seen.Add(key))
                    {
                        result.Add(torrent);
                    }
                }
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

    public int UpdateRuntimeStats(IEnumerable<TorrentRuntimeWrite> writes)
    {
        var list = OrderById(writes);
        if (list.Count == 0)
        {
            return 0;
        }

        // SQLite's scalar max is MAX(a, b). Postgres only has GREATEST for that. Counters move
        // forward, and the status predicate drops a tick that lost a pause/stop race.
        var max = Database.DatabaseType == DatabaseType.PostgreSQL ? "GREATEST" : "MAX";
        var sql = $@"
UPDATE ""{_table}"" SET
    ""Uploaded"" = {max}(""Uploaded"", @Uploaded),
    ""Downloaded"" = {max}(""Downloaded"", @Downloaded),
    ""SeedingTime"" = {max}(""SeedingTime"", @SeedingTime),
    ""SessionUploaded"" = CASE WHEN @Uploaded >= ""Uploaded"" THEN @SessionUploaded ELSE ""SessionUploaded"" END,
    ""SessionDownloaded"" = CASE WHEN @Downloaded >= ""Downloaded"" THEN @SessionDownloaded ELSE ""SessionDownloaded"" END,
    ""Ratio"" = CASE
        WHEN {max}(""Downloaded"", @Downloaded) > 0 THEN ({max}(""Uploaded"", @Uploaded) * 1.0) / {max}(""Downloaded"", @Downloaded)
        WHEN ""TotalSize"" > 0 THEN ({max}(""Uploaded"", @Uploaded) * 1.0) / ""TotalSize""
        ELSE 0 END,
    ""UploadSpeed"" = @UploadSpeed,
    ""DownloadSpeed"" = @DownloadSpeed,
    ""Progress"" = @Progress,
    ""Eta"" = @Eta,
    ""LastActive"" = @LastActive,
    ""Active"" = @Active,
    ""Seeders"" = @Seeders,
    ""Leechers"" = @Leechers,
    ""Availability"" = @Availability,
    ""Threshold"" = CASE WHEN ""Threshold"" = 0 AND @Threshold <> 0 THEN @Threshold ELSE ""Threshold"" END
WHERE ""Id"" = @Id AND ""Status"" = @ExpectedStatus";

        return ExecuteInTransaction(sql, list);
    }

    public int TryTransitionStatuses(IEnumerable<TorrentStatusTransition> transitions)
    {
        var list = OrderById(transitions);
        if (list.Count == 0)
        {
            return 0;
        }

        var sql = $@"
UPDATE ""{_table}"" SET
    ""Status"" = @Status,
    ""Active"" = @Active,
    ""UploadSpeed"" = @UploadSpeed,
    ""DownloadSpeed"" = @DownloadSpeed,
    ""LastActive"" = COALESCE(@LastActive, ""LastActive"")
WHERE ""Id"" = @Id AND ""Status"" = @ExpectedStatus";

        return ExecuteInTransaction(sql, list);
    }

    public void ClearActivity(IEnumerable<int> torrentIds)
    {
        var list = (torrentIds ?? Enumerable.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => new ActivityClear(id))
            .ToList();

        if (list.Count == 0)
        {
            return;
        }

        var sql = $@"UPDATE ""{_table}"" SET ""UploadSpeed"" = 0, ""DownloadSpeed"" = 0, ""Active"" = @Active WHERE ""Id"" = @Id";
        ExecuteInTransaction(sql, list);
    }

    public void UpdateSwarmStats(int torrentId, int seeders, int leechers, double? availability)
    {
        if (torrentId <= 0)
        {
            return;
        }

        var sql = $@"
UPDATE ""{_table}"" SET
    ""Seeders"" = @Seeders,
    ""Leechers"" = @Leechers,
    ""Availability"" = COALESCE(@Availability, ""Availability"")
WHERE ""Id"" = @Id";

        ExecuteWithRetry(connection =>
            connection.Execute(sql, new
            {
                Id = torrentId,
                Seeders = seeders,
                Leechers = leechers,
                Availability = availability
            }));
    }

    public void UpdateExecutionStates(IEnumerable<TorrentExecutionWrite> writes)
    {
        var list = OrderById(writes);
        if (list.Count == 0)
        {
            return;
        }

        var sql = $@"
UPDATE ""{_table}"" SET
    ""Status"" = @Status,
    ""Active"" = @Active,
    ""UploadSpeed"" = @UploadSpeed,
    ""DownloadSpeed"" = @DownloadSpeed,
    ""LastActive"" = CASE WHEN @SetLastActive = 1 THEN @LastActive ELSE ""LastActive"" END,
    ""IsVpnPaused"" = CASE WHEN @SetIsVpnPaused = 1 THEN @IsVpnPaused ELSE ""IsVpnPaused"" END,
    ""ForceStart"" = CASE WHEN @SetForceStart = 1 THEN @ForceStart ELSE ""ForceStart"" END
WHERE ""Id"" = @Id";

        ExecuteInTransaction(sql, list);
    }

    public void UpdateSuperSeeding(int torrentId, bool superSeeding)
    {
        if (torrentId <= 0)
        {
            return;
        }

        ExecuteWithRetry(connection =>
            connection.Execute(
                $@"UPDATE ""{_table}"" SET ""SuperSeeding"" = @SuperSeeding WHERE ""Id"" = @Id",
                new { Id = torrentId, SuperSeeding = superSeeding }));
    }

    public void UpdateSortOrders(IEnumerable<Torrent> torrents)
    {
        var list = (torrents ?? Enumerable.Empty<Torrent>())
            .Where(t => t != null && t.Id > 0)
            .OrderBy(t => t.Id)
            .Select(t => new SortWrite(t.Id, t.SortOrder))
            .ToList();

        if (list.Count == 0)
        {
            return;
        }

        ExecuteInTransaction(
            $@"UPDATE ""{_table}"" SET ""SortOrder"" = @SortOrder WHERE ""Id"" = @Id",
            list);
    }

    public void UpdateUserColumns(Torrent torrent)
    {
        if (torrent == null || torrent.Id <= 0)
        {
            return;
        }

        var sql = $@"
UPDATE ""{_table}"" SET
    ""Name"" = @Name,
    ""Label"" = @Label,
    ""Category"" = @Category,
    ""SavePath"" = @SavePath,
    ""SourcePath"" = @SourcePath,
    ""TagIds"" = @TagIds,
    ""DownloadClientId"" = @DownloadClientId,
    ""Priority"" = @Priority,
    ""UploadLimit"" = @UploadLimit,
    ""DownloadLimit"" = @DownloadLimit,
    ""SuperSeeding"" = @SuperSeeding,
    ""ForceStart"" = @ForceStart,
    ""SequentialDownload"" = @SequentialDownload,
    ""FirstLastPiecePrio"" = @FirstLastPiecePrio,
    ""SmallTorrentLimit"" = @SmallTorrentLimit,
    ""Threshold"" = @Threshold,
    ""RatioLimit"" = @RatioLimit,
    ""SeedingTimeLimit"" = @SeedingTimeLimit,
    ""TrackerUrl"" = @TrackerUrl,
    ""ForceCompleted"" = @ForceCompleted,
    ""Progress"" = @Progress,
    ""Availability"" = @Availability
WHERE ""Id"" = @Id";

        ExecuteWithRetry(connection => connection.Execute(sql, torrent));
    }

    private int ExecuteInTransaction<T>(string sql, IList<T> rows)
    {
        var affected = 0;
        ExecuteWithRetry(connection =>
        {
            using var transaction = connection.BeginTransaction();
            try
            {
                affected = connection.Execute(sql, rows, transaction);
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

        return affected;
    }

    private static List<T> OrderById<T>(IEnumerable<T> rows)
        where T : class
    {
        if (rows == null)
        {
            return new List<T>();
        }

        return rows
            .Where(row => row != null && IdOf(row) > 0)
            .OrderBy(IdOf)
            .ToList();
    }

    private static int IdOf<T>(T row)
    {
        return row switch
        {
            TorrentRuntimeWrite runtime => runtime.Id,
            TorrentStatusTransition transition => transition.Id,
            TorrentExecutionWrite execution => execution.Id,
            _ => 0
        };
    }

    private readonly struct ActivityClear
    {
        public ActivityClear(int id)
        {
            Id = id;
            Active = false;
        }

        public int Id { get; }

        public bool Active { get; }
    }

    private readonly struct SortWrite
    {
        public SortWrite(int id, int sortOrder)
        {
            Id = id;
            SortOrder = sortOrder;
        }

        public int Id { get; }

        public int SortOrder { get; }
    }
}
