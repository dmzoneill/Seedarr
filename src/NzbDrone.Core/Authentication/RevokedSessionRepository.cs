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

        ExecuteWithRetry(connection =>
            connection.Execute(
                $@"
INSERT INTO ""{_table}"" (""SessionKey"", ""RevokedAtUtc"", ""ExpiresAtUtc"")
VALUES (@SessionKey, @RevokedAtUtc, @ExpiresAtUtc)
ON CONFLICT(""SessionKey"") DO UPDATE SET
    ""RevokedAtUtc"" = CASE WHEN excluded.""RevokedAtUtc"" > ""{_table}"".""RevokedAtUtc"" THEN excluded.""RevokedAtUtc"" ELSE ""{_table}"".""RevokedAtUtc"" END,
    ""ExpiresAtUtc"" = CASE WHEN excluded.""RevokedAtUtc"" > ""{_table}"".""RevokedAtUtc"" THEN excluded.""ExpiresAtUtc"" ELSE ""{_table}"".""ExpiresAtUtc"" END",
                new { SessionKey = key, RevokedAtUtc = revokedAtUtc, ExpiresAtUtc = expiresAtUtc }));
    }

    public void DeleteExpired(DateTime revokedBeforeUtc)
    {
        ExecuteWithRetry(connection =>
            connection.Execute(
                $"DELETE FROM \"{_table}\" WHERE \"RevokedAtUtc\" < @Cutoff",
                new { Cutoff = revokedBeforeUtc }));
    }

}
