using System;

namespace NzbDrone.Core.Authentication;

public interface ISessionRevocationService
{
    void RevokeSession(string sessionIdOrUser);
    void RevokeSession(string sessionIdOrUser, DateTime revokedAtUtc);
    bool IsSessionRevoked(string sessionIdOrUser, DateTime issuedUtc);
    void ClearExpired(TimeSpan? maxAge = null);
}
