using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.TrackerServer;

public interface IScrapeCache
{
    TimeSpan DefaultTtl { get; }
    int Count { get; }
    SemaphoreSlim FullScrapeSemaphore { get; }

    bool TryGet(string key, out byte[] payload);
    void Set(string key, byte[] payload, TimeSpan? ttl = null);
    byte[] GetOrCreate(string key, Func<byte[]> factory, TimeSpan? ttl = null);
    Task<byte[]> GetOrCreateAsync(string key, Func<Task<byte[]>> factory, TimeSpan? ttl = null);
    byte[] GetOrCreateFullScrape(Func<byte[]> factory, TimeSpan? ttl = null);
    Task<byte[]> GetOrCreateFullScrapeAsync(Func<Task<byte[]>> factory, TimeSpan? ttl = null);
    void Invalidate(string key);
    void InvalidateAll();
    int PurgeExpired();
}

public class ScrapeCacheEntry
{
    public byte[] Payload { get; }
    public DateTime ExpiresAt { get; set; }

    public ScrapeCacheEntry(byte[] payload, DateTime expiresAt)
    {
        Payload = payload;
        ExpiresAt = expiresAt;
    }

    public bool IsExpired(DateTime now) => now >= ExpiresAt;
}

public class ScrapeCache : IScrapeCache, IDisposable
{
    private readonly ConcurrentDictionary<string, ScrapeCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _fullScrapeSemaphore = new(2, 2);
    private readonly TimeSpan _defaultTtl;
    private bool _disposed;

    public ScrapeCache(TimeSpan? defaultTtl = null)
    {
        _defaultTtl = defaultTtl ?? TimeSpan.FromSeconds(10);
    }

    public TimeSpan DefaultTtl => _defaultTtl;

    public int Count => _cache.Count;

    public SemaphoreSlim FullScrapeSemaphore => _fullScrapeSemaphore;

    public bool TryGet(string key, out byte[] payload)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (!entry.IsExpired(DateTime.UtcNow))
            {
                payload = entry.Payload;
                return true;
            }

            _cache.TryRemove(key, out _);
        }

        payload = null;
        return false;
    }

    public void Set(string key, byte[] payload, TimeSpan? ttl = null)
    {
        var effectiveTtl = ttl ?? _defaultTtl;
        var expiresAt = DateTime.UtcNow.Add(effectiveTtl);
        _cache[key] = new ScrapeCacheEntry(payload, expiresAt);
    }

    public byte[] GetOrCreate(string key, Func<byte[]> factory, TimeSpan? ttl = null)
    {
        if (TryGet(key, out var cached))
        {
            return cached;
        }

        var payload = factory();
        Set(key, payload, ttl);
        return payload;
    }

    public async Task<byte[]> GetOrCreateAsync(string key, Func<Task<byte[]>> factory, TimeSpan? ttl = null)
    {
        if (TryGet(key, out var cached))
        {
            return cached;
        }

        var payload = await factory();
        Set(key, payload, ttl);
        return payload;
    }

    public byte[] GetOrCreateFullScrape(Func<byte[]> factory, TimeSpan? ttl = null)
    {
        const string key = "__full_scrape__";
        if (TryGet(key, out var cached))
        {
            return cached;
        }

        _fullScrapeSemaphore.Wait();
        try
        {
            if (TryGet(key, out cached))
            {
                return cached;
            }

            var payload = factory();
            Set(key, payload, ttl);
            return payload;
        }
        finally
        {
            _fullScrapeSemaphore.Release();
        }
    }

    public async Task<byte[]> GetOrCreateFullScrapeAsync(Func<Task<byte[]>> factory, TimeSpan? ttl = null)
    {
        const string key = "__full_scrape__";
        if (TryGet(key, out var cached))
        {
            return cached;
        }

        await _fullScrapeSemaphore.WaitAsync();
        try
        {
            if (TryGet(key, out cached))
            {
                return cached;
            }

            var payload = await factory();
            Set(key, payload, ttl);
            return payload;
        }
        finally
        {
            _fullScrapeSemaphore.Release();
        }
    }

    public void Invalidate(string key)
    {
        _cache.TryRemove(key, out _);
    }

    public void InvalidateAll()
    {
        _cache.Clear();
    }

    public int PurgeExpired()
    {
        var now = DateTime.UtcNow;
        var count = 0;
        foreach (var kvp in _cache)
        {
            if (kvp.Value.IsExpired(now))
            {
                if (_cache.TryRemove(kvp.Key, out _))
                {
                    count++;
                }
            }
        }

        return count;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _fullScrapeSemaphore.Dispose();
            }

            _disposed = true;
        }
    }
}
