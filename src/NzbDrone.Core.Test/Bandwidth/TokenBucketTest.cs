using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Bandwidth;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Test.Bandwidth;

[TestFixture]
public class TokenBucketTest
{
    [TearDown]
    public void TearDown()
    {
        TokenBucket.PaceWaitHandler = BandwidthPacer.PaceWait;
    }

    [Test]
    public void TokenBucket_should_be_unlimited_when_rate_is_zero_or_negative()
    {
        var bucketZero = new TokenBucket(0);
        Assert.That(bucketZero.IsUnlimited, Is.True);
        Assert.That(bucketZero.TryConsume(1_000_000), Is.True);
        Assert.That(bucketZero.AvailableTokens, Is.EqualTo(double.PositiveInfinity));

        var bucketNeg = new TokenBucket(-100);
        Assert.That(bucketNeg.IsUnlimited, Is.True);
        Assert.That(bucketNeg.TryConsume(5_000_000), Is.True);
    }

    [Test]
    public void TokenBucket_should_clamp_to_max_burst()
    {
        // 100 KB/s rate, max burst clamped explicitly to 5000 bytes
        var bucket = new TokenBucket(100_000, maxBurstBytes: 5_000);
        Assert.That(bucket.MaxBurstBytes, Is.EqualTo(5_000));
        Assert.That(bucket.AvailableTokens, Is.LessThanOrEqualTo(5_000.001));

        // Consuming 6000 exceeds max burst
        Assert.That(bucket.TryConsume(6_000), Is.False);

        // Consuming 5000 should succeed
        Assert.That(bucket.TryConsume(5_000), Is.True);
        Assert.That(bucket.AvailableTokens, Is.LessThan(1_000));
    }

    [Test]
    public void TokenBucket_should_refill_tokens_with_sub_millisecond_precision()
    {
        // 10 MB/s rate -> 10,000 bytes per ms -> 10 bytes per microsecond
        var bucket = new TokenBucket(10_000_000, maxBurstBytes: 100_000, initialTokens: 0);
        Assert.That(bucket.AvailableTokens, Is.LessThan(500));

        // Wait a small interval (~5ms)
        Thread.Sleep(5);

        var refilled = bucket.AvailableTokens;
        Assert.That(refilled, Is.GreaterThan(1_000));
        Assert.That(refilled, Is.LessThanOrEqualTo(100_000));

        // Consuming some tokens should succeed
        var consumeBytes = (long)(refilled / 2);
        Assert.That(bucket.TryConsume(consumeBytes), Is.True);
        Assert.That(bucket.AvailableTokens, Is.LessThan(refilled));
    }

    [Test]
    public void TokenBucket_should_retain_fractional_tokens_without_drift()
    {
        // Low rate: 100 bytes/sec -> 0.1 tokens per millisecond
        var bucket = new TokenBucket(100, maxBurstBytes: 1000, initialTokens: 10.5);

        // Try consuming 10 bytes
        Assert.That(bucket.TryConsume(10), Is.True);

        // Fractional remainder should be retained
        var tokens = bucket.AvailableTokens;
        Assert.That(tokens, Is.GreaterThanOrEqualTo(0.5));
    }

    [Test]
    public void TokenBucket_should_refund_tokens_correctly()
    {
        var bucket = new TokenBucket(10_000, maxBurstBytes: 50_000, initialTokens: 10_000);
        Assert.That(bucket.TryConsume(8_000), Is.True);
        Assert.That(bucket.AvailableTokens, Is.LessThan(3_000));

        bucket.Refund(5_000);
        Assert.That(bucket.AvailableTokens, Is.GreaterThanOrEqualTo(6_500));
    }

    [Test]
    public void TokenBucket_should_be_thread_safe_under_concurrent_access()
    {
        const int totalTokens = 50_000;
        const int threadCount = 10;
        const int consumeChunk = 500;
        // 100 chunks of 500 = 50,000 total tokens
        var bucket = new TokenBucket(bytesPerSecond: 1, maxBurstBytes: totalTokens, initialTokens: totalTokens);

        var successfulConsumes = 0;
        var threads = new List<Thread>();

        for (var i = 0; i < threadCount; i++)
        {
            var t = new Thread(() =>
            {
                for (var j = 0; j < 20; j++)
                {
                    if (bucket.TryConsume(consumeChunk))
                    {
                        Interlocked.Increment(ref successfulConsumes);
                    }
                }
            });
            threads.Add(t);
        }

        foreach (var t in threads)
        {
            t.Start();
        }

        foreach (var t in threads)
        {
            t.Join();
        }

        // Exactly totalTokens / consumeChunk = 100 successful consumes can happen
        Assert.That(successfulConsumes, Is.EqualTo(totalTokens / consumeChunk));
        Assert.That(bucket.AvailableTokens, Is.LessThan(consumeChunk));
    }

    [Test]
    public void TokenBucket_ConsumeOrWait_should_pace_when_tokens_exhausted()
    {
        var bucket = new TokenBucket(10_000, maxBurstBytes: 10_000, initialTokens: 1_000);

        var waitedTicks = 0L;
        TokenBucket.PaceWaitHandler = (start, ticks) =>
        {
            waitedTicks += ticks;
            // Refill tokens artificially in mock wait to let consume complete
            bucket.Refund(2_000);
        };

        bucket.ConsumeOrWait(2_000);
        Assert.That(waitedTicks, Is.GreaterThan(0));
    }

    [Test]
    public async Task TokenBucket_ConsumeOrWaitAsync_should_succeed()
    {
        var bucket = new TokenBucket(100_000, maxBurstBytes: 50_000, initialTokens: 10_000);
        await bucket.ConsumeOrWaitAsync(5_000);
        Assert.That(bucket.AvailableTokens, Is.LessThan(6_000));
    }
}
