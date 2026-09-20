using System;
using System.Diagnostics;
using System.Threading;
using NLog;

namespace NzbDrone.Core.Instrumentation;

/// <summary>
/// Delegate for retrieving worker and I/O completion thread counts.
/// </summary>
internal delegate void ThreadPoolStatsAccessor(out int workerThreads, out int completionPortThreads);

/// <summary>
/// Real-time CPU telemetry service calculating process CPU percentage from elapsed CPU time deltas
/// and querying thread pool saturation metrics.
/// </summary>
public class CpuUsageService : ICpuUsageService
{
    private readonly Logger _logger;
    private readonly object _syncLock = new();
    private readonly Func<TimeSpan> _cpuTimeAccessor;
    private readonly Func<long> _timestampAccessor;
    private readonly long _frequency;
    private readonly int _processorCount;
    private readonly Func<int> _threadCountAccessor;
    private readonly ThreadPoolStatsAccessor _availableThreadsAccessor;
    private readonly ThreadPoolStatsAccessor _maxThreadsAccessor;
    private readonly double _minIntervalSeconds;

    private TimeSpan _lastCpuTime;
    private long _lastTimestamp;
    private double _lastCalculatedUsage;

    public CpuUsageService()
        : this(
            null,
            null,
            Stopwatch.Frequency,
            Environment.ProcessorCount,
            null,
            null,
            null,
            minIntervalSeconds: 0.25)
    {
    }

    internal CpuUsageService(
        Func<TimeSpan> cpuTimeAccessor = null,
        Func<long> timestampAccessor = null,
        long? frequency = null,
        int? processorCount = null,
        Func<int> threadCountAccessor = null,
        ThreadPoolStatsAccessor availableThreadsAccessor = null,
        ThreadPoolStatsAccessor maxThreadsAccessor = null,
        double minIntervalSeconds = 0.25)
    {
        _logger = LogManager.GetCurrentClassLogger();

        _processorCount = processorCount.HasValue && processorCount.Value > 0
            ? processorCount.Value
            : Math.Max(1, Environment.ProcessorCount);

        _frequency = frequency.HasValue && frequency.Value > 0
            ? frequency.Value
            : Stopwatch.Frequency;

        _minIntervalSeconds = Math.Max(0.0, minIntervalSeconds);

        _cpuTimeAccessor = cpuTimeAccessor ?? (() =>
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                return proc.TotalProcessorTime;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to retrieve process total processor time");
                return TimeSpan.Zero;
            }
        });

        _timestampAccessor = timestampAccessor ?? (() => Stopwatch.GetTimestamp());

        _threadCountAccessor = threadCountAccessor ?? (() =>
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                return proc.Threads.Count;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to retrieve process thread count");
                return 0;
            }
        });

        _availableThreadsAccessor = availableThreadsAccessor ?? ((out int w, out int c) => ThreadPool.GetAvailableThreads(out w, out c));
        _maxThreadsAccessor = maxThreadsAccessor ?? ((out int w, out int c) => ThreadPool.GetMaxThreads(out w, out c));

        lock (_syncLock)
        {
            _lastCpuTime = _cpuTimeAccessor();
            _lastTimestamp = _timestampAccessor();
            _lastCalculatedUsage = 0.0;
        }
    }

    public double GetCpuUsagePercentage()
    {
        lock (_syncLock)
        {
            var currentTimestamp = _timestampAccessor();
            var elapsedTicks = currentTimestamp - _lastTimestamp;
            var elapsedSeconds = (double)elapsedTicks / _frequency;

            if (elapsedSeconds < _minIntervalSeconds)
            {
                return _lastCalculatedUsage;
            }

            var currentCpuTime = _cpuTimeAccessor();
            var cpuDeltaSeconds = (currentCpuTime - _lastCpuTime).TotalSeconds;

            if (cpuDeltaSeconds < 0)
            {
                cpuDeltaSeconds = 0;
            }

            var usage = (cpuDeltaSeconds / (elapsedSeconds * _processorCount)) * 100.0;
            var clamped = Math.Clamp(usage, 0.0, 100.0);
            var rounded = Math.Round(clamped, 1);

            _lastCpuTime = currentCpuTime;
            _lastTimestamp = currentTimestamp;
            _lastCalculatedUsage = rounded;

            return rounded;
        }
    }

    public int GetProcessorCount()
    {
        return _processorCount;
    }

    public int GetThreadCount()
    {
        try
        {
            return _threadCountAccessor();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error executing thread count accessor");
            return 0;
        }
    }

    public (int AvailableWorker, int AvailableCompletionPort, int MaxWorker, int MaxCompletionPort) GetThreadPoolStats()
    {
        try
        {
            _availableThreadsAccessor(out var availWorker, out var availCompletion);
            _maxThreadsAccessor(out var maxWorker, out var maxCompletion);
            return (availWorker, availCompletion, maxWorker, maxCompletion);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error retrieving thread pool statistics");
            return (0, 0, 0, 0);
        }
    }
}
