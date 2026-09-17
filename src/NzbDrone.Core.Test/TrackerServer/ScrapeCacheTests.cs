using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.TrackerServer;

namespace NzbDrone.Core.Test.TrackerServer;

[TestFixture]
public class ScrapeCacheTests
{
    private ScrapeCache _cache;

    [SetUp]
    public void SetUp()
    {
        _cache = new ScrapeCache(TimeSpan.FromSeconds(10));
    }

    [TearDown]
    public void TearDown()
    {
        _cache?.Dispose();
    }

    [Test]
    public void GetOrCreate_should_return_cached_payload_within_ttl_without_reinvoking_factory()
    {
        var callCount = 0;
        byte[] Factory()
        {
            callCount++;
            return Encoding.ASCII.GetBytes($"response_{callCount}");
        }

        var result1 = _cache.GetOrCreate("hash1", Factory);
        var result2 = _cache.GetOrCreate("hash1", Factory);

        Assert.That(callCount, Is.EqualTo(1));
        Assert.That(result1, Is.EqualTo(result2));
        Assert.That(Encoding.ASCII.GetString(result1), Is.EqualTo("response_1"));
    }

    [Test]
    public void GetOrCreate_should_expire_after_ttl_and_reinvoke_factory()
    {
        using var shortCache = new ScrapeCache(TimeSpan.FromMilliseconds(50));
        var callCount = 0;
        byte[] Factory()
        {
            callCount++;
            return Encoding.ASCII.GetBytes($"response_{callCount}");
        }

        var result1 = shortCache.GetOrCreate("hash1", Factory);
        Assert.That(callCount, Is.EqualTo(1));
        Assert.That(Encoding.ASCII.GetString(result1), Is.EqualTo("response_1"));

        Thread.Sleep(100);

        var result2 = shortCache.GetOrCreate("hash1", Factory);
        Assert.That(callCount, Is.EqualTo(2));
        Assert.That(Encoding.ASCII.GetString(result2), Is.EqualTo("response_2"));
    }

    [Test]
    public void GetOrCreateFullScrape_should_enforce_concurrency_limit_of_two()
    {
        using var cache = new ScrapeCache(TimeSpan.FromMilliseconds(10));
        var currentConcurrent = 0;
        var maxConcurrent = 0;
        var lockObj = new object();
        var callCount = 0;

        var tasks = new List<Task<byte[]>>();
        for (var i = 0; i < 8; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                return cache.GetOrCreateFullScrape(() =>
                {
                    var current = Interlocked.Increment(ref currentConcurrent);
                    lock (lockObj)
                    {
                        if (current > maxConcurrent)
                        {
                            maxConcurrent = current;
                        }
                    }

                    Interlocked.Increment(ref callCount);
                    Thread.Sleep(50);
                    Interlocked.Decrement(ref currentConcurrent);
                    return Encoding.ASCII.GetBytes("full_scrape_payload");
                });
            }));
        }

        Task.WaitAll(tasks.ToArray());

        Assert.That(maxConcurrent, Is.LessThanOrEqualTo(2));
        Assert.That(maxConcurrent, Is.GreaterThanOrEqualTo(1));
        foreach (var task in tasks)
        {
            Assert.That(Encoding.ASCII.GetString(task.Result), Is.EqualTo("full_scrape_payload"));
        }
    }

    [Test]
    public void GetOrCreateFullScrape_should_return_cached_payload_within_ttl_without_reinvoking_factory()
    {
        var callCount = 0;
        byte[] Factory()
        {
            callCount++;
            return Encoding.ASCII.GetBytes($"full_scrape_{callCount}");
        }

        var result1 = _cache.GetOrCreateFullScrape(Factory);
        var result2 = _cache.GetOrCreateFullScrape(Factory);

        Assert.That(callCount, Is.EqualTo(1));
        Assert.That(result1, Is.EqualTo(result2));
        Assert.That(Encoding.ASCII.GetString(result1), Is.EqualTo("full_scrape_1"));
    }

    [Test]
    public void Invalidate_should_remove_specific_entry()
    {
        _cache.Set("hash1", Encoding.ASCII.GetBytes("val1"));
        _cache.Set("hash2", Encoding.ASCII.GetBytes("val2"));

        _cache.Invalidate("hash1");

        Assert.That(_cache.TryGet("hash1", out _), Is.False);
        Assert.That(_cache.TryGet("hash2", out var val2), Is.True);
        Assert.That(Encoding.ASCII.GetString(val2), Is.EqualTo("val2"));
    }

    [Test]
    public void InvalidateAll_should_remove_all_entries()
    {
        _cache.Set("hash1", Encoding.ASCII.GetBytes("val1"));
        _cache.Set("hash2", Encoding.ASCII.GetBytes("val2"));

        _cache.InvalidateAll();

        Assert.That(_cache.Count, Is.EqualTo(0));
        Assert.That(_cache.TryGet("hash1", out _), Is.False);
        Assert.That(_cache.TryGet("hash2", out _), Is.False);
    }

    [Test]
    public void PurgeExpired_should_purge_only_expired_entries()
    {
        _cache.Set("expired", Encoding.ASCII.GetBytes("expired_val"), TimeSpan.FromMilliseconds(20));
        _cache.Set("active", Encoding.ASCII.GetBytes("active_val"), TimeSpan.FromSeconds(30));

        Thread.Sleep(60);

        var purged = _cache.PurgeExpired();

        Assert.That(purged, Is.GreaterThanOrEqualTo(1));
        Assert.That(_cache.TryGet("expired", out _), Is.False);
        Assert.That(_cache.TryGet("active", out var activeVal), Is.True);
        Assert.That(Encoding.ASCII.GetString(activeVal), Is.EqualTo("active_val"));
    }

    [Test]
    public async Task GetOrCreateAsync_should_support_async_factory()
    {
        var callCount = 0;
        async Task<byte[]> FactoryAsync()
        {
            await Task.Delay(10);
            callCount++;
            return Encoding.ASCII.GetBytes($"async_response_{callCount}");
        }

        var result1 = await _cache.GetOrCreateAsync("async_key", FactoryAsync);
        var result2 = await _cache.GetOrCreateAsync("async_key", FactoryAsync);

        Assert.That(callCount, Is.EqualTo(1));
        Assert.That(result1, Is.EqualTo(result2));
        Assert.That(Encoding.ASCII.GetString(result1), Is.EqualTo("async_response_1"));
    }

    [Test]
    public async Task GetOrCreateFullScrapeAsync_should_support_async_factory_and_cache_result()
    {
        var callCount = 0;
        async Task<byte[]> FactoryAsync()
        {
            await Task.Delay(10);
            callCount++;
            return Encoding.ASCII.GetBytes($"async_full_{callCount}");
        }

        var result1 = await _cache.GetOrCreateFullScrapeAsync(FactoryAsync);
        var result2 = await _cache.GetOrCreateFullScrapeAsync(FactoryAsync);

        Assert.That(callCount, Is.EqualTo(1));
        Assert.That(result1, Is.EqualTo(result2));
        Assert.That(Encoding.ASCII.GetString(result1), Is.EqualTo("async_full_1"));
    }
}
