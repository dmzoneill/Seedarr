using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Torrents;

public class DownloadHistoryRepository : BasicRepository<DownloadHistory>, IDownloadHistoryRepository
{
    private readonly IDatabase _database;

    public DownloadHistoryRepository(IDatabase database)
        : base(database)
    {
        _database = database;
    }

    public DownloadHistory FindByInfoHash(string infoHash)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<DownloadHistory>(
            $"SELECT * FROM \"{_table}\" WHERE \"InfoHash\" = @InfoHash ORDER BY \"Id\" DESC",
            new { InfoHash = infoHash });
    }

    public DownloadHistory FindByTorrentId(int torrentId)
    {
        using var connection = _database.OpenConnection();
        return connection.QueryFirstOrDefault<DownloadHistory>(
            $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId ORDER BY \"Id\" DESC",
            new { TorrentId = torrentId });
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

        using var connection = _database.OpenConnection();
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
    }

    public List<DownloadHistory> GetHistory(string query = null, string status = null, int limit = 500, int offset = 0)
    {
        using var connection = _database.OpenConnection();
        var sql = new StringBuilder($"SELECT * FROM \"{_table}\" WHERE 1=1");
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query))
        {
            sql.Append(" AND (\"Title\" LIKE @Query OR \"InfoHash\" LIKE @Query OR \"PrimaryTracker\" LIKE @Query OR \"IndexerName\" LIKE @Query)");
            parameters.Add("Query", $"%{query.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", System.StringComparison.OrdinalIgnoreCase))
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
    }

    public int DeleteOlderThan(DateTime cutoffDate)
    {
        using var connection = _database.OpenConnection();
        return connection.Execute(
            $"DELETE FROM \"{_table}\" WHERE \"DateAdded\" < @Cutoff",
            new { Cutoff = cutoffDate });
    }

    public void DeleteAll()
    {
        using var connection = _database.OpenConnection();
        connection.Execute($"DELETE FROM \"{_table}\"");
    }
}
