using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Bandwidth;

public class TokenBucket : ITokenBucket
{
    public const double DefaultBurstDurationSeconds = 0.100; // 100ms
    public const long DefaultMinBurstBytes = 16 * 1024;       // 16 KB

    private long _rate;
    private long _maxBurstBytes;
    private BucketState _state;

    public static Action<long, long> PaceWaitHandler { get; set; } = BandwidthPacer.PaceWait;

    private sealed class BucketState
    {
        public readonly double Tokens;
        public readonly long LastRefillTicks;

        public BucketState(double tokens, long lastRefillTicks)
        {
            Tokens = tokens;
            LastRefillTicks = lastRefillTicks;
        }
    }

    public TokenBucket(long bytesPerSecond = 0, long? maxBurstBytes = null, double? initialTokens = null)
    {
        if (bytesPerSecond <= 0)
        {
            _rate = 0;
            _maxBurstBytes = long.MaxValue;
            _state = new BucketState(0, Stopwatch.GetTimestamp());
        }
        else
        {
            _rate = bytesPerSecond;
            var burst = maxBurstBytes ?? CalculateDefaultMaxBurst(bytesPerSecond);
            _maxBurstBytes = Math.Max(1, burst);
            var initial = initialTokens.HasValue ? Math.Clamp(initialTokens.Value, 0, _maxBurstBytes) : _maxBurstBytes;
            _state = new BucketState(initial, Stopwatch.GetTimestamp());
        }
    }

    public static long CalculateDefaultMaxBurst(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return long.MaxValue;
        }

        var burst = (long)Math.Round(bytesPerSecond * DefaultBurstDurationSeconds);
        return Math.Max(DefaultMinBurstBytes, burst);
    }

    public long Rate
    {
        get => Volatile.Read(ref _rate);
        set => SetRate(value);
    }

    public long MaxBurstBytes => Volatile.Read(ref _maxBurstBytes);

    public bool IsUnlimited => Volatile.Read(ref _rate) <= 0;

    public double AvailableTokens
    {
        get
        {
            var rate = Volatile.Read(ref _rate);
            if (rate <= 0)
            {
                return double.PositiveInfinity;
            }

            var current = Volatile.Read(ref _state);
            var nowTicks = Stopwatch.GetTimestamp();
            var elapsedTicks = Math.Max(0, nowTicks - current.LastRefillTicks);
            var elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
            var maxBurst = Volatile.Read(ref _maxBurstBytes);

            return Math.Min(maxBurst, current.Tokens + (elapsedSeconds * rate));
        }
    }

    public void SetRate(long bytesPerSecond, long? maxBurstBytes = null)
    {
        if (bytesPerSecond <= 0)
        {
            Volatile.Write(ref _rate, 0);
            Volatile.Write(ref _maxBurstBytes, long.MaxValue);
            return;
        }

        var burst = maxBurstBytes ?? CalculateDefaultMaxBurst(bytesPerSecond);
        var clampedBurst = Math.Max(1, burst);
        Volatile.Write(ref _maxBurstBytes, clampedBurst);
        Volatile.Write(ref _rate, bytesPerSecond);

        SpinWait spinWait = default;
        while (true)
        {
            var current = Volatile.Read(ref _state);
            var nextTokens = current.Tokens <= 0 ? clampedBurst : Math.Min(clampedBurst, current.Tokens);
            var nextState = new BucketState(nextTokens, Stopwatch.GetTimestamp());
            if (Interlocked.CompareExchange(ref _state, nextState, current) == current)
            {
                break;
            }

            spinWait.SpinOnce();
        }
    }

    public bool TryConsume(long bytes)
    {
        if (bytes <= 0 || Volatile.Read(ref _rate) <= 0)
        {
            return true;
        }

        SpinWait spinWait = default;
        while (true)
        {
            var current = Volatile.Read(ref _state);
            var currentRate = Volatile.Read(ref _rate);
            if (currentRate <= 0)
            {
                return true;
            }

            var nowTicks = Stopwatch.GetTimestamp();
            var elapsedTicks = Math.Max(0, nowTicks - current.LastRefillTicks);
            var elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
            var maxBurst = Volatile.Read(ref _maxBurstBytes);

            var refilledTokens = Math.Min((double)maxBurst, current.Tokens + (elapsedSeconds * currentRate));

            if (refilledTokens < bytes)
            {
                if (refilledTokens > current.Tokens)
                {
                    var updateState = new BucketState(refilledTokens, nowTicks);
                    Interlocked.CompareExchange(ref _state, updateState, current);
                }

                return false;
            }

            var nextTokens = refilledTokens - bytes;
            var nextState = new BucketState(nextTokens, nowTicks);

            if (Interlocked.CompareExchange(ref _state, nextState, current) == current)
            {
                return true;
            }

            spinWait.SpinOnce();
        }
    }

    public void ConsumeOrWait(long bytes)
    {
        if (bytes <= 0 || Volatile.Read(ref _rate) <= 0)
        {
            return;
        }

        var remaining = bytes;
        while (remaining > 0)
        {
            var maxBurst = Volatile.Read(ref _maxBurstBytes);
            var chunk = Math.Min(remaining, Math.Max(1, maxBurst));

            while (!TryConsume(chunk))
            {
                var rate = Volatile.Read(ref _rate);
                if (rate <= 0)
                {
                    return;
                }

                var available = AvailableTokens;
                var deficit = Math.Max(1.0, chunk - available);
                var waitTicks = (long)Math.Ceiling(deficit * Stopwatch.Frequency / rate);
                if (waitTicks <= 0)
                {
                    waitTicks = 1;
                }

                PaceWait(waitTicks);
            }

            remaining -= chunk;
        }
    }

    public async Task ConsumeOrWaitAsync(long bytes, CancellationToken cancellationToken = default)
    {
        if (bytes <= 0 || Volatile.Read(ref _rate) <= 0)
        {
            return;
        }

        var remaining = bytes;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var maxBurst = Volatile.Read(ref _maxBurstBytes);
            var chunk = Math.Min(remaining, Math.Max(1, maxBurst));

            while (!TryConsume(chunk))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rate = Volatile.Read(ref _rate);
                if (rate <= 0)
                {
                    return;
                }

                var available = AvailableTokens;
                var deficit = Math.Max(1.0, chunk - available);
                var waitSeconds = deficit / rate;
                var waitMs = (int)Math.Ceiling(waitSeconds * 1000.0);

                if (waitMs > 1)
                {
                    await Task.Delay(waitMs, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var waitTicks = (long)Math.Ceiling(deficit * Stopwatch.Frequency / rate);
                    PaceWait(waitTicks);
                }
            }

            remaining -= chunk;
        }
    }

    public void Refund(long bytes)
    {
        if (bytes <= 0 || Volatile.Read(ref _rate) <= 0)
        {
            return;
        }

        SpinWait spinWait = default;
        while (true)
        {
            var current = Volatile.Read(ref _state);
            var nowTicks = Stopwatch.GetTimestamp();
            var elapsedTicks = Math.Max(0, nowTicks - current.LastRefillTicks);
            var elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
            var currentRate = Volatile.Read(ref _rate);
            var maxBurst = Volatile.Read(ref _maxBurstBytes);

            var refilledTokens = current.Tokens + (elapsedSeconds * currentRate);
            var available = Math.Min(maxBurst, refilledTokens + bytes);

            var nextState = new BucketState(available, nowTicks);
            if (Interlocked.CompareExchange(ref _state, nextState, current) == current)
            {
                return;
            }

            spinWait.SpinOnce();
        }
    }

    public void Reset()
    {
        var rate = Volatile.Read(ref _rate);
        var maxBurst = Volatile.Read(ref _maxBurstBytes);
        var initial = rate <= 0 ? 0 : maxBurst;
        Volatile.Write(ref _state, new BucketState(initial, Stopwatch.GetTimestamp()));
    }

    private static void PaceWait(long waitTicks)
    {
        var start = Stopwatch.GetTimestamp();
        PaceWaitHandler(start, waitTicks);
    }
}
