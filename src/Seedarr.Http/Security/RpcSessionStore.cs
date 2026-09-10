using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Seedarr.Http.Security;

public class RpcSessionStore
{
    private readonly ConcurrentDictionary<string, DateTime> _sessions = new();
    private readonly int _maxCapacity;
    private readonly object _pruneLock = new();

    public RpcSessionStore(int maxCapacity = 10000)
    {
        _maxCapacity = Math.Max(10, maxCapacity);
    }

    public int Count => _sessions.Count;

    public bool IsValid(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (_sessions.TryGetValue(token, out var expiry))
        {
            if (expiry > DateTime.UtcNow)
            {
                return true;
            }

            _sessions.TryRemove(token, out _);
        }

        return false;
    }

    public void SetSession(string token, DateTime expiry)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        PruneExpired();

        if (_sessions.Count >= _maxCapacity)
        {
            lock (_pruneLock)
            {
                if (_sessions.Count >= _maxCapacity)
                {
                    var excess = _sessions.Count - _maxCapacity + 1;
                    var oldestKeys = _sessions
                        .OrderBy(kvp => kvp.Value)
                        .Take(excess)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in oldestKeys)
                    {
                        _sessions.TryRemove(key, out _);
                    }
                }
            }
        }

        _sessions[token] = expiry;
    }

    public void SetSession(string token, TimeSpan lifetime)
    {
        SetSession(token, DateTime.UtcNow.Add(lifetime));
    }

    public bool RemoveSession(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return _sessions.TryRemove(token, out _);
    }

    public bool TryGetValue(string token, out DateTime expiry)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            expiry = default;
            return false;
        }

        if (_sessions.TryGetValue(token, out expiry))
        {
            if (expiry > DateTime.UtcNow)
            {
                return true;
            }

            _sessions.TryRemove(token, out _);
        }

        expiry = default;
        return false;
    }

    public void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value <= now)
            {
                _sessions.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Clear()
    {
        _sessions.Clear();
    }
}
