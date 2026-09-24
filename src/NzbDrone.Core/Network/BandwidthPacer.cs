using System;
using System.Diagnostics;
using System.Threading;

namespace NzbDrone.Core.Network;

public static class BandwidthPacer
{
    public const int MinThrottledBufferSize = 8 * 1024;
    public const int MaxThrottledBufferSize = 64 * 1024;
    public const int DefaultUnthrottledBufferSize = 256 * 1024;
    public const double DefaultEstimatedRttSeconds = 0.1;
    public const int DefaultPacingChunkSize = 2 * 1024;
    public const int DefaultPacketPacingIntervalMicroseconds = 50;

    /// <summary>
    /// Calculates the dynamic BDP (Bandwidth-Delay Product) socket buffer size.
    /// Clamped between 8KB and 64KB when throttled (rate > 0), and returns a larger buffer when unthrottled (rate &lt;= 0).
    /// </summary>
    public static int CalculateBdpBufferSize(
        long rateBytesPerSec,
        double rttSeconds = DefaultEstimatedRttSeconds,
        int minThrottled = MinThrottledBufferSize,
        int maxThrottled = MaxThrottledBufferSize,
        int unthrottled = DefaultUnthrottledBufferSize)
    {
        if (rateBytesPerSec <= 0)
        {
            return unthrottled;
        }

        if (rttSeconds <= 0)
        {
            rttSeconds = DefaultEstimatedRttSeconds;
        }

        var bdp = (long)Math.Ceiling(rateBytesPerSec * rttSeconds);
        return (int)Math.Clamp(bdp, minThrottled, maxThrottled);
    }

    /// <summary>
    /// Waits with high-resolution precision until target ticks have elapsed since startTimestamp.
    /// Uses sleep for coarse durations and spin-wait for sub-millisecond precision.
    /// </summary>
    public static void PaceWait(long startTimestamp, long targetTicks)
    {
        if (targetTicks <= 0)
        {
            return;
        }

        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        var remainingTicks = targetTicks - elapsed;
        if (remainingTicks <= 0)
        {
            return;
        }

        var remainingMs = (remainingTicks * 1000) / Stopwatch.Frequency;
        if (remainingMs > 2)
        {
            Thread.Sleep((int)(remainingMs - 1));
        }

        while (Stopwatch.GetTimestamp() - startTimestamp < targetTicks)
        {
            Thread.SpinWait(10);
        }
    }
}
