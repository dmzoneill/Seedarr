using System;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.System;

/// <summary>
/// API resource representing system status information.
/// </summary>
public class SystemResource : RestResource
{
    /// <summary>
    /// Gets or sets the application name.
    /// </summary>
    public string AppName { get; set; }

    /// <summary>
    /// Gets or sets the application version.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Gets or sets the OS name.
    /// </summary>
    public string OsName { get; set; }

    /// <summary>
    /// Gets or sets the OS version.
    /// </summary>
    public string OsVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the OS is Windows.
    /// </summary>
    public bool IsWindows { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the OS is Linux.
    /// </summary>
    public bool IsLinux { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the OS is macOS.
    /// </summary>
    public bool IsOsx { get; set; }

    /// <summary>
    /// Gets or sets the branch name.
    /// </summary>
    public string Branch { get; set; }

    /// <summary>
    /// Gets or sets the runtime name.
    /// </summary>
    public string RuntimeName { get; set; }

    /// <summary>
    /// Gets or sets the runtime version string.
    /// </summary>
    public string RuntimeVersion { get; set; }

    /// <summary>
    /// Gets or sets the startup time.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the startup directory path.
    /// </summary>
    public string StartupPath { get; set; }

    /// <summary>
    /// Gets or sets the application data directory path.
    /// </summary>
    public string AppDataPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether running in Docker.
    /// </summary>
    public bool IsDocker { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether running in debug mode.
    /// </summary>
    public bool IsDebug { get; set; }

    /// <summary>
    /// Gets or sets the database engine version string.
    /// </summary>
    public string DatabaseVersion { get; set; }

    /// <summary>
    /// Gets or sets the last database migration identifier.
    /// </summary>
    public string DatabaseMigration { get; set; }

    /// <summary>
    /// Gets or sets the unique instance identifier UUID.
    /// </summary>
    public string InstanceUuid { get; set; }

    /// <summary>
    /// Gets or sets the application uptime in seconds.
    /// </summary>
    public double UptimeSeconds { get; set; }

    /// <summary>
    /// Gets or sets the Gen 0 garbage collection count.
    /// </summary>
    public int GcGen0Collections { get; set; }

    /// <summary>
    /// Gets or sets the Gen 1 garbage collection count.
    /// </summary>
    public int GcGen1Collections { get; set; }

    /// <summary>
    /// Gets or sets the Gen 2 garbage collection count.
    /// </summary>
    public int GcGen2Collections { get; set; }

    /// <summary>
    /// Gets or sets the lifetime total allocated bytes by the managed runtime.
    /// </summary>
    public long GcTotalAllocatedBytes { get; set; }

    /// <summary>
    /// Gets or sets the managed heap size in bytes.
    /// </summary>
    public long GcHeapSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the percentage of time spent in GC pauses.
    /// </summary>
    public double GcPauseTimePercentage { get; set; }

    /// <summary>
    /// Gets or sets the count of open file descriptors or operating system handles.
    /// </summary>
    public int OpenFileDescriptors { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed file descriptors, or -1 if unbounded.
    /// </summary>
    public int MaxFileDescriptors { get; set; }

    /// <summary>
    /// Gets or sets the percentage of file descriptors currently in use, if bounded.
    /// </summary>
    public double? FileDescriptorUsagePercentage { get; set; }
}
