using System;
using System.Collections.Concurrent;
using System.Threading;
using NLog;

namespace NzbDrone.Core.Authentication;

public class LoginRateLimiter : ILoginRateLimiter
{
    private class AttemptRecord
    {
        public int FailedAttempts { get; set; }

        public DateTime LastFailedUtc { get; set; }

        public DateTime? LockedOutUntilUtc { get; set; }
    }

    private readonly ConcurrentDictionary<string, AttemptRecord> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private int _cleanupCounter;

    public int MaxFailedAttempts { get; }

    public TimeSpan LockoutDuration { get; }

    public TimeSpan FailureWindow { get; }

    public LoginRateLimiter(
        int maxFailedAttempts = 5,
        TimeSpan? lockoutDuration = null,
        TimeSpan? failureWindow = null)
    {
        MaxFailedAttempts = maxFailedAttempts > 0 ? maxFailedAttempts : 5;
        LockoutDuration = lockoutDuration ?? TimeSpan.FromMinutes(15);
        FailureWindow = failureWindow ?? TimeSpan.FromMinutes(15);
    }

    public bool IsRateLimited(string ipAddress, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        var key = ipAddress.Trim();
        if (!_attempts.TryGetValue(key, out var record))
        {
            return false;
        }

        lock (record)
        {
            var now = DateTime.UtcNow;

            if (record.LockedOutUntilUtc.HasValue)
            {
                if (record.LockedOutUntilUtc.Value > now)
                {
                    retryAfter = record.LockedOutUntilUtc.Value - now;
                    return true;
                }

                // Lockout has expired; reset record
                record.FailedAttempts = 0;
                record.LockedOutUntilUtc = null;
                return false;
            }

            if (now - record.LastFailedUtc > FailureWindow)
            {
                record.FailedAttempts = 0;
                return false;
            }

            return false;
        }
    }

    public void RecordFailedAttempt(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return;
        }

        var key = ipAddress.Trim();
        var record = _attempts.GetOrAdd(key, _ => new AttemptRecord());

        lock (record)
        {
            var now = DateTime.UtcNow;

            if (record.LockedOutUntilUtc.HasValue && record.LockedOutUntilUtc.Value > now)
            {
                return;
            }

            if (now - record.LastFailedUtc > FailureWindow)
            {
                record.FailedAttempts = 0;
                record.LockedOutUntilUtc = null;
            }

            record.FailedAttempts++;
            record.LastFailedUtc = now;

            if (record.FailedAttempts >= MaxFailedAttempts)
            {
                record.LockedOutUntilUtc = now.Add(LockoutDuration);
                _logger.Warn(
                    "IP address {0} exceeded maximum failed login attempts ({1}) and is locked out until {2:u}",
                    key,
                    record.FailedAttempts,
                    record.LockedOutUntilUtc.Value);
            }
            else
            {
                _logger.Debug(
                    "Failed login attempt {0}/{1} for IP {2}",
                    record.FailedAttempts,
                    MaxFailedAttempts,
                    key);
            }
        }

        MaybeCleanup();
    }

    public void RecordSuccessfulLogin(string ipAddress)
    {
        Reset(ipAddress);
    }

    public void Reset(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return;
        }

        var key = ipAddress.Trim();
        _attempts.TryRemove(key, out _);
    }

    public int GetFailedAttempts(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return 0;
        }

        var key = ipAddress.Trim();
        if (_attempts.TryGetValue(key, out var record))
        {
            lock (record)
            {
                var now = DateTime.UtcNow;

                if (record.LockedOutUntilUtc.HasValue && record.LockedOutUntilUtc.Value <= now)
                {
                    return 0;
                }

                if (now - record.LastFailedUtc > FailureWindow)
                {
                    return 0;
                }

                return record.FailedAttempts;
            }
        }

        return 0;
    }

    public void Clear()
    {
        _attempts.Clear();
    }

    public void ClearExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _attempts)
        {
            var record = kvp.Value;
            lock (record)
            {
                var isExpired = (!record.LockedOutUntilUtc.HasValue || record.LockedOutUntilUtc.Value <= now)
                                && (now - record.LastFailedUtc > FailureWindow);
                if (isExpired)
                {
                    _attempts.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    private void MaybeCleanup()
    {
        if (Interlocked.Increment(ref _cleanupCounter) % 100 == 0)
        {
            ClearExpired();
        }
    }
}
