using System;
using System.Collections.Concurrent;
using NLog;

namespace NzbDrone.Core.Authentication;

public class SessionRevocationService : ISessionRevocationService
{
    private readonly ConcurrentDictionary<string, DateTime> _revokedSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRevokedSessionRepository _repository;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    public SessionRevocationService(IRevokedSessionRepository repository = null)
    {
        _repository = repository;
        LoadActiveRevocations();
    }

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
        var expiresAtUtc = AuthenticationDefaults.GetRevocationExpiresAtUtc(revokedAtUtc);

        if (_repository != null)
        {
            _repository.Upsert(key, revokedAtUtc, expiresAtUtc);
        }

        _revokedSessions.AddOrUpdate(
            key,
            revokedAtUtc,
            (_, existing) => revokedAtUtc > existing ? revokedAtUtc : existing);

        _logger.Debug("Revoked session or user token: {0} at {1:u}", key, revokedAtUtc);
    }

    public bool IsSessionRevoked(string sessionIdOrUser, DateTime issuedUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionIdOrUser))
        {
            return false;
        }

        var key = sessionIdOrUser.Trim();
        var issuedUtcUnknown = issuedUtc == default || issuedUtc == DateTime.MinValue;

        if (!TryGetActiveRevocation(key, out var revokedAtUtc))
        {
            return false;
        }

        if (issuedUtcUnknown)
        {
            return true;
        }

        return issuedUtc <= revokedAtUtc;
    }

    private bool TryGetActiveRevocation(string key, out DateTime revokedAtUtc)
    {
        if (_revokedSessions.TryGetValue(key, out revokedAtUtc))
        {
            return true;
        }

        if (_repository == null)
        {
            revokedAtUtc = default;
            return false;
        }

        try
        {
            var entry = _repository.GetActiveBySessionKey(key, DateTime.UtcNow);
            if (entry == null || string.IsNullOrWhiteSpace(entry.SessionKey))
            {
                revokedAtUtc = default;
                return false;
            }

            revokedAtUtc = entry.RevokedAtUtc;
            _revokedSessions.AddOrUpdate(
                key,
                revokedAtUtc,
                (_, existing) => revokedAtUtc > existing ? revokedAtUtc : existing);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to check revoked session {0} in database", key);
            revokedAtUtc = default;
            return false;
        }
    }

    public void ClearExpired(TimeSpan? maxAge = null)
    {
        var retention = maxAge ?? AuthenticationDefaults.RevocationRetention;
        var cutoff = DateTime.UtcNow - retention;

        foreach (var kvp in _revokedSessions)
        {
            if (kvp.Value < cutoff)
            {
                _revokedSessions.TryRemove(kvp.Key, out _);
            }
        }

        _repository?.DeleteExpired(DateTime.UtcNow);
    }

    private void LoadActiveRevocations()
    {
        if (_repository == null)
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            foreach (var entry in _repository.GetActive(now))
            {
                if (string.IsNullOrWhiteSpace(entry.SessionKey))
                {
                    continue;
                }

                var key = entry.SessionKey.Trim();
                _revokedSessions.AddOrUpdate(
                    key,
                    entry.RevokedAtUtc,
                    (_, existing) => entry.RevokedAtUtc > existing ? entry.RevokedAtUtc : existing);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to load revoked sessions from database; continuing with in-memory cache only");
        }
    }
}
