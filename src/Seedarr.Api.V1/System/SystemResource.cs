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
    /// Gets or sets the application uptime in seconds.
    /// </summary>
    public double UptimeSeconds { get; set; }
}
