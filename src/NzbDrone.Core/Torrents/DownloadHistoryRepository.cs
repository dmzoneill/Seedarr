using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryRepository : BasicRepository<DownloadHistory>, IDownloadHistoryRepository
{
    public DownloadHistoryRepository(IDatabase database)
        : base(database)
    {
    }

    public DownloadHistory FindByInfoHash(string infoHash)
    {
        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<DownloadHistory>(
                $"SELECT * FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash ORDER BY \"Id\" DESC",
                new { InfoHash = infoHash }));
    }

    public DownloadHistory FindByTorrentId(int torrentId)
    {
        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<DownloadHistory>(
                $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Id\" DESC",
                new { TorrentId = torrentId }));
    }

    public Dictionary<string, DownloadHistory> GetLatestByInfoHashes(IEnumerable<string> infoHashes)
    {
        if (infoHashes == null)
        {
            return new Dictionary<string, DownloadHistory>(StringComparer.OrdinalIgnoreCase);
        }

        var hashes = infoHashes.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToList();
        if (hashes.Count == 0)
        {
            return new Dictionary<string, DownloadHistory>(StringComparer.OrdinalIgnoreCase);
        }

        return QueryWithRetry(connection =>
        {
            var dict = new Dictionary<string, DownloadHistory>(StringComparer.OrdinalIgnoreCase);

            foreach (var batch in hashes.Chunk(500))
            {
                var records = connection.Query<DownloadHistory>(
                    $"SELECT * FROM \"{_table}\" WHERE \"Id\" IN (SELECT MAX(\"Id\") FROM \"{_table}\" WHERE \"InfoHash\" IN @Hashes GROUP BY \"InfoHash\")",
                    new { Hashes = batch });

                foreach (var record in records)
                {
                    if (!string.IsNullOrWhiteSpace(record.InfoHash))
                    {
                        dict[record.InfoHash] = record;
                    }
                }
            }

            return dict;
        });
    }

    public List<DownloadHistory> GetHistory(string query = null, string status = null, int limit = 500, int offset = 0)
    {
        return QueryWithRetry(connection =>
        {
            var sql = new StringBuilder($"SELECT * FROM \"{_table}\" WHERE 1=1");
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(query))
            {
                sql.Append(" AND (\"Title\" LIKE @Query OR \"InfoHash\" LIKE @Query OR \"PrimaryTracker\" LIKE @Query OR \"IndexerName\" LIKE @Query)");
                parameters.Add("Query", $"%{query.Trim()}%");
            }

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                sql.Append(" AND \"Status\" = @Status");
                parameters.Add("Status", status.Trim());
            }

            sql.Append(" ORDER BY \"DateAdded\" DESC");

            if (limit > 0)
            {
                sql.Append(" LIMIT @Limit");
                parameters.Add("Limit", limit);
            }

            if (offset > 0)
            {
                sql.Append(" OFFSET @Offset");
                parameters.Add("Offset", offset);
            }

            return connection.Query<DownloadHistory>(sql.ToString(), parameters).ToList();
        });
    }

    public int DeleteOlderThan(DateTime cutoffDate) => DeleteOlderThan(cutoffDate, 1000);

    public int DeleteOlderThan(DateTime cutoffDate, int batchSize)
    {
        var clampedBatchSize = batchSize <= 0 ? 1000 : batchSize;

        return QueryWithRetry(connection =>
        {
            var totalDeleted = 0;

            while (true)
            {
                var rowsAffected = connection.Execute(
                    $@"DELETE FROM ""{_table}""
                    WHERE ""Id"" IN (
                        SELECT ""Id"" FROM ""{_table}""
                        WHERE ""DateAdded"" < @Cutoff
                            AND (""Status"" IS NULL OR LOWER(""Status"") NOT IN ('active', 'seeding'))
                            AND (
                                (""DateRemoved"" IS NOT NULL AND ""DateRemoved"" < @Cutoff)
                                OR (""DateRemoved"" IS NULL AND (""TorrentId"" IS NULL OR LOWER(""Status"") IN ('removed', 'inactive')))
                            )
                        LIMIT @BatchSize
                    )",
                    new { Cutoff = cutoffDate, BatchSize = clampedBatchSize });

                totalDeleted += rowsAffected;
                if (rowsAffected < clampedBatchSize)
                {
                    break;
                }

                Thread.Sleep(1);
            }

            return totalDeleted;
        });
    }

    public void DeleteAll()
    {
        ExecuteWithRetry(connection =>
        {
            connection.Execute($"DELETE FROM \"{_table}\"");
        });
    }
}
