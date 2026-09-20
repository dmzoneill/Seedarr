using System;
using System.Collections.Concurrent;
using NLog;

namespace NzbDrone.Core.Authentication;

public class SessionRevocationService : ISessionRevocationService
{
    private readonly ConcurrentDictionary<string, DateTime> _revokedSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(30);
    private int _operationCount;

    public void RevokeSession(string sessionIdOrUser)
    {
        RevokeSession(sessionIdOrUser, DateTime.UtcNow);
    }

    public void RevokeSession(string sessionIdOrUser, DateTime revokedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionIdOrUser))
        {
            return;
        }

        var key = sessionIdOrUser.Trim();
        _revokedSessions.AddOrUpdate(
            key,
            revokedAtUtc,
            (_, existing) => revokedAtUtc > existing ? revokedAtUtc : existing);

        _logger.Debug("Revoked session or user token: {0} at {1:u}", key, revokedAtUtc);

        if (System.Threading.Interlocked.Increment(ref _operationCount) % 100 == 0)
        {
            ClearExpired();
        }
    }

    public bool IsSessionRevoked(string sessionIdOrUser, DateTime issuedUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionIdOrUser))
        {
            return false;
        }

        var key = sessionIdOrUser.Trim();
        if (_revokedSessions.TryGetValue(key, out var revokedAtUtc))
        {
            // If the ticket was issued at or before the revocation timestamp, it is revoked.
            return issuedUtc <= revokedAtUtc;
        }

        return false;
    }

    public void ClearExpired(TimeSpan? maxAge = null)
    {
        var retention = maxAge ?? DefaultRetention;
        var cutoff = DateTime.UtcNow - retention;

        foreach (var kvp in _revokedSessions)
        {
            if (kvp.Value < cutoff)
            {
                _revokedSessions.TryRemove(kvp.Key, out _);
            }
        }
    }
}
