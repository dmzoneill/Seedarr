using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Seedarr.Http.Security;

public interface IRpcSessionStore
{
    int Count { get; }

    bool IsValid(string token);

    void SetSession(string token, DateTime expiry);

    void SetSession(string token, TimeSpan lifetime);

    bool RemoveSession(string token);

    bool TryGetValue(string token, out DateTime expiry);

    void PruneExpired();

    void Clear();

    void InvalidateAll();
}

public class RpcSessionStore : IRpcSessionStore
{
    private static readonly Lazy<IRpcSessionStore> SharedInstance = new(() => new RpcSessionStore());

    private readonly ConcurrentDictionary<string, DateTime> _sessions = new();
    private readonly int _maxCapacity;
    private readonly object _pruneLock = new();
    private static readonly long PruneIntervalTicks = TimeSpan.FromSeconds(5).Ticks;
    private long _lastPruneTicks;

    public RpcSessionStore(int maxCapacity = 10000)
    {
        _maxCapacity = Math.Max(10, maxCapacity);
        _lastPruneTicks = DateTime.UtcNow.Ticks;
    }

    public static IRpcSessionStore SharedSessionStore => SharedInstance.Value;

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

        var nowTicks = DateTime.UtcNow.Ticks;
        if (_sessions.Count >= _maxCapacity || nowTicks - Volatile.Read(ref _lastPruneTicks) > PruneIntervalTicks)
        {
            PruneExpiredThrottled(nowTicks);
        }

        if (_sessions.Count >= _maxCapacity)
        {
            EvictSampledAtCapacity();
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
        Volatile.Write(ref _lastPruneTicks, DateTime.UtcNow.Ticks);
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

    public void InvalidateAll()
    {
        Clear();
    }

    private void PruneExpiredThrottled(long nowTicks)
    {
        var last = Volatile.Read(ref _lastPruneTicks);
        if (nowTicks - last < PruneIntervalTicks && _sessions.Count < _maxCapacity)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastPruneTicks, nowTicks, last) != last)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value <= now)
            {
                _sessions.TryRemove(kvp.Key, out _);
            }
        }
    }

    private void EvictSampledAtCapacity()
    {
        lock (_pruneLock)
        {
            var now = DateTime.UtcNow;
            while (_sessions.Count >= _maxCapacity && !_sessions.IsEmpty)
            {
                var excess = _sessions.Count - _maxCapacity + 1;
                var sample = new List<KeyValuePair<string, DateTime>>(64);

                using (var enumerator = _sessions.GetEnumerator())
                {
                    while (enumerator.MoveNext() && sample.Count < 64)
                    {
                        sample.Add(enumerator.Current);
                    }
                }

                if (sample.Count == 0)
                {
                    break;
                }

                var evicted = 0;

                // 1. Remove expired entries from the sample first
                for (var i = 0; i < sample.Count && evicted < excess; i++)
                {
                    if (sample[i].Value <= now && _sessions.TryRemove(sample[i].Key, out _))
                    {
                        evicted++;
                    }
                }

                // 2. Remove oldest from the sample if still at or over capacity
                if (evicted < excess)
                {
                    sample.Sort(static (a, b) => a.Value.CompareTo(b.Value));
                    for (var i = 0; i < sample.Count && evicted < excess; i++)
                    {
                        if (_sessions.TryRemove(sample[i].Key, out _))
                        {
                            evicted++;
                        }
                    }
                }

                if (evicted == 0)
                {
                    break;
                }
            }
        }
    }
}
