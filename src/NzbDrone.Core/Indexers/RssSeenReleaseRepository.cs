using System;
using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Indexers;

public class RssSeenReleaseRepository : BasicRepository<RssSeenRelease>, IRssSeenReleaseRepository
{
    public RssSeenReleaseRepository(IDatabase database)
        : base(database)
    {
    }

    public bool IsSeen(int indexerId, string guid, string infoHash = null)
    {
        var cleanGuid = string.IsNullOrWhiteSpace(guid) ? null : guid.Trim();
        var cleanHash = string.IsNullOrWhiteSpace(infoHash) ? null : infoHash.Trim().ToLowerInvariant();

        if (cleanGuid == null && cleanHash == null)
        {
            return false;
        }

        return QueryWithRetry(connection =>
        {
            if (cleanGuid != null && cleanHash != null)
            {
                return connection.ExecuteScalar<int>(
                    $"SELECT COUNT(1) FROM \"{_table}\" WHERE \"IndexerId\" = @IndexerId AND ((\"Guid\" = @Guid AND \"Guid\" != '') OR (\"InfoHash\" = @InfoHash AND \"InfoHash\" IS NOT NULL))",
                    new { IndexerId = indexerId, Guid = cleanGuid, InfoHash = cleanHash }) > 0;
            }

            if (cleanGuid != null)
            {
                return connection.ExecuteScalar<int>(
                    $"SELECT COUNT(1) FROM \"{_table}\" WHERE \"IndexerId\" = @IndexerId AND \"Guid\" = @Guid AND \"Guid\" != ''",
                    new { IndexerId = indexerId, Guid = cleanGuid }) > 0;
            }

            return connection.ExecuteScalar<int>(
                $"SELECT COUNT(1) FROM \"{_table}\" WHERE \"IndexerId\" = @IndexerId AND \"InfoHash\" = @InfoHash AND \"InfoHash\" IS NOT NULL",
                new { IndexerId = indexerId, InfoHash = cleanHash }) > 0;
        });
    }

    public HashSet<string> GetSeenGuids(int indexerId)
    {
        return QueryWithRetry(connection =>
        {
            var guids = connection.Query<string>(
                $"SELECT \"Guid\" FROM \"{_table}\" WHERE \"IndexerId\" = @IndexerId AND \"Guid\" IS NOT NULL AND \"Guid\" != ''",
                new { IndexerId = indexerId });

            return new HashSet<string>(guids, StringComparer.OrdinalIgnoreCase);
        });
    }

    public HashSet<string> GetSeenInfoHashes(int indexerId)
    {
        return QueryWithRetry(connection =>
        {
            var hashes = connection.Query<string>(
                $"SELECT \"InfoHash\" FROM \"{_table}\" WHERE \"IndexerId\" = @IndexerId AND \"InfoHash\" IS NOT NULL AND \"InfoHash\" != ''",
                new { IndexerId = indexerId });

            return new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);
        });
    }

    public void MarkSeen(int indexerId, ReleaseInfo release, RssSeenStatus status, int? matchedRuleId = null)
    {
        if (release == null)
        {
            return;
        }

        var guid = !string.IsNullOrWhiteSpace(release.Guid)
            ? release.Guid.Trim()
            : (!string.IsNullOrWhiteSpace(release.DownloadUrl) ? release.DownloadUrl.Trim() : string.Empty);
        var infoHash = string.IsNullOrWhiteSpace(release.InfoHash) ? null : release.InfoHash.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(guid) && string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        if (IsSeen(indexerId, guid, infoHash))
        {
            ExecuteWithRetry(connection =>
            {
                connection.Execute(
                    $"UPDATE \"{_table}\" SET \"Status\" = @Status, \"MatchedRuleId\" = @MatchedRuleId WHERE \"IndexerId\" = @IndexerId AND ((\"Guid\" = @Guid AND @Guid != '') OR (\"InfoHash\" = @InfoHash AND @InfoHash IS NOT NULL))",
                    new
                    {
                        Status = (int)status,
                        MatchedRuleId = matchedRuleId,
                        IndexerId = indexerId,
                        Guid = guid,
                        InfoHash = infoHash
                    });
            });
            return;
        }

        var seen = new RssSeenRelease
        {
            IndexerId = indexerId,
            Guid = guid,
            InfoHash = infoHash,
            PublishDate = release.PublishDate,
            FirstSeenUtc = DateTime.UtcNow,
            Status = status,
            MatchedRuleId = matchedRuleId
        };

        Insert(seen);
    }

    public void MarkSeenBatch(int indexerId, IEnumerable<ReleaseInfo> releases, RssSeenStatus status)
    {
        if (releases == null)
        {
            return;
        }

        var seenGuids = GetSeenGuids(indexerId);
        var seenHashes = GetSeenInfoHashes(indexerId);

        var now = DateTime.UtcNow;
        var toInsert = new List<RssSeenRelease>();

        foreach (var release in releases)
        {
            if (release == null)
            {
                continue;
            }

            var guid = !string.IsNullOrWhiteSpace(release.Guid)
                ? release.Guid.Trim()
                : (!string.IsNullOrWhiteSpace(release.DownloadUrl) ? release.DownloadUrl.Trim() : string.Empty);
            var infoHash = string.IsNullOrWhiteSpace(release.InfoHash) ? null : release.InfoHash.Trim().ToLowerInvariant();

            var hasGuid = !string.IsNullOrWhiteSpace(guid);
            var hasInfoHash = !string.IsNullOrWhiteSpace(infoHash);

            if (!hasGuid && !hasInfoHash)
            {
                continue;
            }

            if ((hasGuid && seenGuids.Contains(guid)) || (hasInfoHash && seenHashes.Contains(infoHash)))
            {
                continue;
            }

            toInsert.Add(new RssSeenRelease
            {
                IndexerId = indexerId,
                Guid = guid,
                InfoHash = infoHash,
                PublishDate = release.PublishDate,
                FirstSeenUtc = now,
                Status = status,
                MatchedRuleId = null
            });

            if (hasGuid)
            {
                seenGuids.Add(guid);
            }

            if (hasInfoHash)
            {
                seenHashes.Add(infoHash);
            }
        }

        if (toInsert.Count > 0)
        {
            InsertMany(toInsert);
        }
    }

    public int PurgeOlderThan(TimeSpan age)
    {
        var cutoff = DateTime.UtcNow.Subtract(age);
        return PurgeOlderThan(cutoff);
    }

    public int PurgeOlderThan(DateTime cutoff)
    {
        return QueryWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{_table}\" WHERE \"FirstSeenUtc\" < @Cutoff",
                new { Cutoff = cutoff }));
    }
}
