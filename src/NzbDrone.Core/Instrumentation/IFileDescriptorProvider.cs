namespace NzbDrone.Core.Instrumentation;

/// <summary>
/// Provides metrics on open file descriptors and operating system handles.
/// </summary>
public interface IFileDescriptorProvider
{
    /// <summary>
    /// Gets the count of currently open file descriptors or process handles.
    /// </summary>
    /// <returns>Count of open descriptors/handles, or -1 if unavailable.</returns>
    int GetOpenFileDescriptorCount();

    /// <summary>
    /// Gets the maximum allowed open file descriptors (soft limit), or -1 if unbounded / unavailable.
    /// </summary>
    /// <returns>Max open file descriptors, or -1 if unbounded.</returns>
    int GetMaxFileDescriptors();

    /// <summary>
    /// Gets the file descriptor usage percentage, or null if unbounded / unavailable.
    /// </summary>
    /// <returns>Percentage (0.0 to 100.0+), or null.</returns>
    double? GetFileDescriptorUsagePercentage();
}
