namespace NzbDrone.Core.Instrumentation;

/// <summary>
/// Provides real-time process CPU utilization and thread pool saturation metrics.
/// </summary>
public interface ICpuUsageService
{
    /// <summary>
    /// Gets the instantaneous process CPU usage percentage (clamped to 0.0 - 100.0).
    /// </summary>
    double GetCpuUsagePercentage();

    /// <summary>
    /// Gets the number of logical processors available to the process.
    /// </summary>
    int GetProcessorCount();

    /// <summary>
    /// Gets the count of active threads within the process.
    /// </summary>
    int GetThreadCount();

    /// <summary>
    /// Gets current thread pool statistics including available and max worker and completion port threads.
    /// </summary>
    (int AvailableWorker, int AvailableCompletionPort, int MaxWorker, int MaxCompletionPort) GetThreadPoolStats();
}
