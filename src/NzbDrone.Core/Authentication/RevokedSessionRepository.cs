using System;
using System.Collections.Generic;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public class RevokedSessionRepository : BasicRepository<RevokedSession>, IRevokedSessionRepository
{
    public RevokedSessionRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<RevokedSession> GetActive(DateTime utcNow)
    {
        return QueryWithRetry(connection =>
            connection.Query<RevokedSession>(
                $"SELECT * FROM \"{_table}\" WHERE \"ExpiresAtUtc\" > @UtcNow",
                new { UtcNow = utcNow }));
    }

    public void Upsert(string sessionKey, DateTime revokedAtUtc, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            return;
        }

        var key = sessionKey.Trim();
        var existing = FindByKey(key);
        if (existing == null)
        {
            Insert(new RevokedSession
            {
                SessionKey = key,
                RevokedAtUtc = revokedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
            });
            return;
        }

        if (revokedAtUtc > existing.RevokedAtUtc)
        {
            existing.RevokedAtUtc = revokedAtUtc;
            existing.ExpiresAtUtc = expiresAtUtc;
            Update(existing);
        }
    }

    public void DeleteExpired(DateTime revokedBeforeUtc)
    {
        ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{_table}\" WHERE \"RevokedAtUtc\" < @Cutoff",
                new { Cutoff = revokedBeforeUtc }));
    }

    private RevokedSession FindByKey(string sessionKey)
    {
        return QueryWithRetry(connection =>
            connection.QueryFirstOrDefault<RevokedSession>(
                $"SELECT * FROM \"{_table}\" WHERE \"SessionKey\" = @SessionKey COLLATE NOCASE",
                new { SessionKey = sessionKey }));
    }
}
