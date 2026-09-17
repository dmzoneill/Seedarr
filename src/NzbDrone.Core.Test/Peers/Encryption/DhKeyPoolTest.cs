using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Peers.Encryption;

namespace NzbDrone.Core.Test.Peers.Encryption;

[TestFixture]
public class DhKeyPoolTest
{
    [Test]
    public void Rent_should_fallback_to_generating_key_when_pool_is_empty()
    {
        using var pool = new DhKeyPool(4);

        Assert.That(pool.AvailableCount, Is.EqualTo(0));

        var key = pool.Rent();

        Assert.That(key, Is.Not.Null);
        var pubKey = key.GetPublicKeyBytes();
        Assert.That(pubKey, Is.Not.Null);
        Assert.That(pubKey.Length, Is.EqualTo(96));
        Assert.That(pool.AvailableCount, Is.EqualTo(0));
    }

    [Test]
    public async Task ReplenishAsync_should_populate_pool_up_to_target_size()
    {
        const int targetSize = 3;
        using var pool = new DhKeyPool(targetSize);

        Assert.That(pool.AvailableCount, Is.EqualTo(0));

        await pool.ReplenishAsync();

        Assert.That(pool.AvailableCount, Is.EqualTo(targetSize));
    }

    [Test]
    public async Task Rent_should_take_precomputed_key_from_pool()
    {
        const int targetSize = 2;
        using var pool = new DhKeyPool(targetSize);
        await pool.ReplenishAsync();

        Assert.That(pool.AvailableCount, Is.EqualTo(targetSize));

        var key1 = pool.Rent();
        Assert.That(key1, Is.Not.Null);
        Assert.That(pool.AvailableCount, Is.EqualTo(1));

        var key2 = pool.Rent();
        Assert.That(key2, Is.Not.Null);
        Assert.That(pool.AvailableCount, Is.EqualTo(0));

        // When drained, falls back to new key
        var key3 = pool.Rent();
        Assert.That(key3, Is.Not.Null);
        Assert.That(pool.AvailableCount, Is.EqualTo(0));
    }

    [Test]
    public async Task ReplenishAsync_should_honor_cancellation_token()
    {
        using var pool = new DhKeyPool(10);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await pool.ReplenishAsync(cts.Token);

        Assert.That(pool.AvailableCount, Is.EqualTo(0));
    }

    [Test]
    public async Task BackgroundService_should_replenish_pool_and_recover_after_rent()
    {
        const int targetSize = 2;
        using var pool = new DhKeyPool(targetSize);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await pool.StartAsync(cts.Token);

        // Wait for background worker to populate pool
        var timeout = DateTime.UtcNow.AddSeconds(4);
        while (pool.AvailableCount < targetSize && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20, cts.Token);
        }

        Assert.That(pool.AvailableCount, Is.EqualTo(targetSize));

        // Rent one key
        var key = pool.Rent();
        Assert.That(key, Is.Not.Null);

        // Background service should replenish back to targetSize
        timeout = DateTime.UtcNow.AddSeconds(4);
        while (pool.AvailableCount < targetSize && DateTime.UtcNow < timeout)
        {
            await Task.Delay(20, cts.Token);
        }

        Assert.That(pool.AvailableCount, Is.EqualTo(targetSize));

        await pool.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task Concurrent_Rent_should_return_unique_keys_without_conflicts()
    {
        const int count = 8;
        using var pool = new DhKeyPool(count);
        await pool.ReplenishAsync();

        var tasks = Enumerable.Range(0, count)
            .Select(_ => Task.Run(() => pool.Rent()))
            .ToArray();

        var keys = await Task.WhenAll(tasks);

        var pubKeyStrings = new HashSet<string>();
        foreach (var k in keys)
        {
            Assert.That(k, Is.Not.Null);
            var pubKey = Convert.ToHexString(k.GetPublicKeyBytes());
            Assert.That(pubKeyStrings.Add(pubKey), Is.True, "Public key must be unique");
        }
    }

    [Test]
    public void Custom_target_size_should_be_respected()
    {
        using var poolDefault = new DhKeyPool();
        Assert.That(poolDefault.TargetPoolSize, Is.EqualTo(DhKeyPool.DefaultTargetPoolSize));

        using var poolCustom = new DhKeyPool(16);
        Assert.That(poolCustom.TargetPoolSize, Is.EqualTo(16));

        using var poolInvalid = new DhKeyPool(-5);
        Assert.That(poolInvalid.TargetPoolSize, Is.EqualTo(DhKeyPool.DefaultTargetPoolSize));
    }
}
