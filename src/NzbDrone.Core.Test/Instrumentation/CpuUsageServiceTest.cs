using System;
using NUnit.Framework;
using NzbDrone.Core.Instrumentation;

namespace NzbDrone.Core.Test.Instrumentation;

[TestFixture]
public class CpuUsageServiceTest
{
    [Test]
    public void GetProcessorCount_returns_configured_count()
    {
        var service = new CpuUsageService(processorCount: 8);
        Assert.That(service.GetProcessorCount(), Is.EqualTo(8));
    }

    [Test]
    public void GetProcessorCount_defaults_to_environment_processor_count()
    {
        var service = new CpuUsageService();
        Assert.That(service.GetProcessorCount(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void GetThreadCount_returns_value_from_accessor()
    {
        var service = new CpuUsageService(threadCountAccessor: () => 42);
        Assert.That(service.GetThreadCount(), Is.EqualTo(42));
    }

    [Test]
    public void GetThreadCount_returns_zero_on_exception()
    {
        var service = new CpuUsageService(threadCountAccessor: () => throw new InvalidOperationException("Process terminated"));
        Assert.That(service.GetThreadCount(), Is.EqualTo(0));
    }

    [Test]
    public void GetThreadPoolStats_returns_thread_pool_metrics()
    {
        ThreadPoolStatsAccessor availAccessor = (out int w, out int c) =>
        {
            w = 32760;
            c = 1000;
        };

        ThreadPoolStatsAccessor maxAccessor = (out int w, out int c) =>
        {
            w = 32767;
            c = 1024;
        };

        var service = new CpuUsageService(
            availableThreadsAccessor: availAccessor,
            maxThreadsAccessor: maxAccessor);

        var stats = service.GetThreadPoolStats();

        Assert.That(stats.AvailableWorker, Is.EqualTo(32760));
        Assert.That(stats.AvailableCompletionPort, Is.EqualTo(1000));
        Assert.That(stats.MaxWorker, Is.EqualTo(32767));
        Assert.That(stats.MaxCompletionPort, Is.EqualTo(1024));
    }

    [Test]
    public void GetThreadPoolStats_handles_exception_gracefully()
    {
        ThreadPoolStatsAccessor failingAccessor = (out int w, out int c) => throw new InvalidOperationException("Failed");

        var service = new CpuUsageService(availableThreadsAccessor: failingAccessor);
        var stats = service.GetThreadPoolStats();

        Assert.That(stats.AvailableWorker, Is.EqualTo(0));
        Assert.That(stats.AvailableCompletionPort, Is.EqualTo(0));
        Assert.That(stats.MaxWorker, Is.EqualTo(0));
        Assert.That(stats.MaxCompletionPort, Is.EqualTo(0));
    }

    [Test]
    public void GetCpuUsagePercentage_calculates_usage_percentage_from_deltas()
    {
        var currentCpu = TimeSpan.FromSeconds(10);
        long currentTimestamp = 0;
        const long frequency = 1000; // 1000 ticks = 1 second
        const int cores = 4;

        var service = new CpuUsageService(
            cpuTimeAccessor: () => currentCpu,
            timestampAccessor: () => currentTimestamp,
            frequency: frequency,
            processorCount: cores,
            minIntervalSeconds: 0.1);

        // Advance by 1 second (1000 ticks).
        // 1 second of CPU used across 4 cores = 25% CPU usage.
        currentTimestamp = 1000;
        currentCpu = TimeSpan.FromSeconds(11);

        var usage = service.GetCpuUsagePercentage();

        Assert.That(usage, Is.EqualTo(25.0));
    }

    [Test]
    public void GetCpuUsagePercentage_clamps_to_100_percent()
    {
        var currentCpu = TimeSpan.FromSeconds(0);
        long currentTimestamp = 0;
        const long frequency = 1000;
        const int cores = 2;

        var service = new CpuUsageService(
            cpuTimeAccessor: () => currentCpu,
            timestampAccessor: () => currentTimestamp,
            frequency: frequency,
            processorCount: cores,
            minIntervalSeconds: 0.1);

        // Advance by 1 second, but 4 seconds of CPU time used on 2 cores (theoretical 200%)
        currentTimestamp = 1000;
        currentCpu = TimeSpan.FromSeconds(4);

        var usage = service.GetCpuUsagePercentage();

        Assert.That(usage, Is.EqualTo(100.0));
    }

    [Test]
    public void GetCpuUsagePercentage_clamps_negative_delta_to_zero()
    {
        var currentCpu = TimeSpan.FromSeconds(10);
        long currentTimestamp = 0;
        const long frequency = 1000;
        const int cores = 2;

        var service = new CpuUsageService(
            cpuTimeAccessor: () => currentCpu,
            timestampAccessor: () => currentTimestamp,
            frequency: frequency,
            processorCount: cores,
            minIntervalSeconds: 0.1);

        // Negative CPU delta (e.g. system clock or counter anomaly)
        currentTimestamp = 1000;
        currentCpu = TimeSpan.FromSeconds(8);

        var usage = service.GetCpuUsagePercentage();

        Assert.That(usage, Is.EqualTo(0.0));
    }

    [Test]
    public void GetCpuUsagePercentage_returns_cached_usage_when_within_min_interval()
    {
        var currentCpu = TimeSpan.FromSeconds(0);
        long currentTimestamp = 0;
        const long frequency = 1000;
        const int cores = 4;

        var service = new CpuUsageService(
            cpuTimeAccessor: () => currentCpu,
            timestampAccessor: () => currentTimestamp,
            frequency: frequency,
            processorCount: cores,
            minIntervalSeconds: 1.0);

        // First sample after 1 second: 50% CPU
        currentTimestamp = 1000;
        currentCpu = TimeSpan.FromSeconds(2);
        var firstUsage = service.GetCpuUsagePercentage();
        Assert.That(firstUsage, Is.EqualTo(50.0));

        // Subsequent call only 100ms later (less than 1.0s min interval)
        currentTimestamp = 1100;
        currentCpu = TimeSpan.FromSeconds(10);
        var cachedUsage = service.GetCpuUsagePercentage();

        Assert.That(cachedUsage, Is.EqualTo(50.0));
    }

    [Test]
    public void Default_constructor_runs_with_live_process_telemetry()
    {
        var service = new CpuUsageService();

        var usage = service.GetCpuUsagePercentage();
        var cores = service.GetProcessorCount();
        var threads = service.GetThreadCount();
        var pool = service.GetThreadPoolStats();

        Assert.That(usage, Is.InRange(0.0, 100.0));
        Assert.That(cores, Is.GreaterThan(0));
        Assert.That(threads, Is.GreaterThanOrEqualTo(0));
        Assert.That(pool.MaxWorker, Is.GreaterThan(0));
    }
}
