using System;

namespace NzbDrone.Core.Authentication;

public static class AuthenticationDefaults
{
    /// <summary>
    /// Cookie authentication expire window (must match Startup cookie options).
    /// </summary>
    public static readonly TimeSpan CookieExpireTimeSpan = TimeSpan.FromDays(30);

    /// <summary>
    /// How long revocations are kept after logout. Two cookie windows covers a sliding ticket that was
    /// at full lifetime when revoked and could otherwise be replayed once after a single-window purge.
    /// </summary>
    public static readonly TimeSpan RevocationRetention = TimeSpan.FromDays(60);

    public static DateTime GetRevocationExpiresAtUtc(DateTime revokedAtUtc)
    {
        return revokedAtUtc + RevocationRetention;
    }
}
