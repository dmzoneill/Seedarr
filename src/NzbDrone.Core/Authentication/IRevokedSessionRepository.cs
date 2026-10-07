using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication;

public interface IRevokedSessionRepository : IBasicRepository<RevokedSession>
{
    IEnumerable<RevokedSession> GetActive(DateTime utcNow);

    RevokedSession GetActiveBySessionKey(string sessionKey, DateTime utcNow);

    void Upsert(string sessionKey, DateTime revokedAtUtc, DateTime expiresAtUtc);

    void DeleteExpired(DateTime revokedBeforeUtc);
}
