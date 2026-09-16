using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.DiskSpace;

/// <summary>
/// Health states for disk space monitoring.
/// </summary>
public enum DiskSpaceHealthState
{
    Normal,
    Low,
    Critical,
}

/// <summary>
/// Provides disk space information for relevant locations.
/// </summary>
public interface IDiskSpaceService
{
    /// <summary>
    /// Returns disk space information for all relevant locations.
    /// </summary>
    /// <returns>A list of disk space information.</returns>
    List<DiskSpaceInfo> GetDiskSpace();

    /// <summary>
    /// Gets the current health state for the specified drive path.
    /// </summary>
    DiskSpaceHealthState GetHealthState(string path);

    /// <summary>
    /// Updates and evaluates disk space health state for a path.
    /// </summary>
    DiskSpaceHealthState UpdateDiskSpaceHealth(string path, long freeSpace, long totalSpace);

    /// <summary>
    /// Clears any cached health states.
    /// </summary>
    void ResetHealthStates();
}

/// <summary>
/// Default implementation of <see cref="IDiskSpaceService"/>.
/// </summary>
public class DiskSpaceService : IDiskSpaceService
{
    public const long CriticalBytesThreshold = 1024L * 1024 * 1024; // 1 GB
    public const long CriticalRecoveryBytesThreshold = 1280L * 1024 * 1024; // 1.25 GB
    public const long LowBytesThreshold = 5L * 1024 * 1024 * 1024; // 5 GB
    public const double LowPercentThreshold = 0.05; // 5%
    public const long NormalRecoveryBytesThreshold = 6L * 1024 * 1024 * 1024; // 6 GB
    public const double NormalRecoveryPercentThreshold = 0.06; // 6%

    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, DiskSpaceHealthState> _healthStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskSpaceService"/> class.
    /// </summary>
    /// <param name="appFolderInfo">Application folder information.</param>
    /// <param name="eventAggregator">Event aggregator for publishing disk space threshold events.</param>
    public DiskSpaceService(IAppFolderInfo appFolderInfo, IEventAggregator eventAggregator = null)
    {
        _appFolderInfo = appFolderInfo;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    /// <inheritdoc/>
    public DiskSpaceHealthState GetHealthState(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DiskSpaceHealthState.Normal;
        }

        return _healthStates.TryGetValue(path, out var state) ? state : DiskSpaceHealthState.Normal;
    }

    /// <inheritdoc/>
    public void ResetHealthStates()
    {
        _healthStates.Clear();
    }

    /// <inheritdoc/>
    public DiskSpaceHealthState UpdateDiskSpaceHealth(string path, long freeSpace, long totalSpace)
    {
        if (string.IsNullOrWhiteSpace(path) || totalSpace <= 0)
        {
            return DiskSpaceHealthState.Normal;
        }

        var freePercentage = (double)freeSpace / totalSpace;
        DiskSpaceHealthState oldState;
        DiskSpaceHealthState newState;
        bool transitionOccurred = false;

        lock (_stateLock)
        {
            oldState = _healthStates.GetOrAdd(path, DiskSpaceHealthState.Normal);
            newState = ComputeNextHealthState(oldState, freeSpace, totalSpace, freePercentage);

            if (newState != oldState)
            {
                _healthStates[path] = newState;
                transitionOccurred = true;
            }
        }

        if (transitionOccurred && _eventAggregator != null)
        {
            PublishHealthTransitionEvent(path, oldState, newState, freeSpace, totalSpace, freePercentage);
        }

        return newState;
    }

    /// <inheritdoc/>
    public List<DiskSpaceInfo> GetDiskSpace()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to enumerate drives");
            drives = Array.Empty<DriveInfo>();
        }

        AddDriveInfoWithDrives(result, seen, _appFolderInfo.AppDataFolder, "AppData", drives);
        AddDriveInfoWithDrives(result, seen, _appFolderInfo.StartUpFolder, "Startup", drives);

        foreach (var drive in drives)
        {
            try
            {
                if (seen.Add(drive.RootDirectory.FullName))
                {
                    var info = new DiskSpaceInfo
                    {
                        Path = drive.RootDirectory.FullName,
                        Label = drive.VolumeLabel.Length > 0 ? drive.VolumeLabel : drive.RootDirectory.FullName,
                        FreeSpace = drive.AvailableFreeSpace,
                        TotalSpace = drive.TotalSize,
                    };
                    result.Add(info);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get space for drive {0}", drive.Name);
            }
        }

        foreach (var info in result)
        {
            if (info.TotalSpace > 0)
            {
                UpdateDiskSpaceHealth(info.Path, info.FreeSpace, info.TotalSpace);
            }
        }

        return result;
    }

    private DiskSpaceHealthState ComputeNextHealthState(
        DiskSpaceHealthState currentState,
        long freeSpace,
        long totalSpace,
        double freePercentage)
    {
        switch (currentState)
        {
            case DiskSpaceHealthState.Critical:
                if (IsNormalRecovery(freeSpace, totalSpace, freePercentage))
                {
                    return DiskSpaceHealthState.Normal;
                }

                if (freeSpace >= CriticalRecoveryBytesThreshold)
                {
                    return DiskSpaceHealthState.Low;
                }

                return DiskSpaceHealthState.Critical;

            case DiskSpaceHealthState.Low:
                if (freeSpace < CriticalBytesThreshold)
                {
                    return DiskSpaceHealthState.Critical;
                }

                if (IsNormalRecovery(freeSpace, totalSpace, freePercentage))
                {
                    return DiskSpaceHealthState.Normal;
                }

                return DiskSpaceHealthState.Low;

            case DiskSpaceHealthState.Normal:
            default:
                if (freeSpace < CriticalBytesThreshold)
                {
                    return DiskSpaceHealthState.Critical;
                }

                if (IsLow(freeSpace, totalSpace, freePercentage))
                {
                    return DiskSpaceHealthState.Low;
                }

                return DiskSpaceHealthState.Normal;
        }
    }

    private static bool IsLow(long freeSpace, long totalSpace, double freePercentage)
    {
        if (totalSpace > 0 && totalSpace < LowBytesThreshold)
        {
            return freePercentage < LowPercentThreshold;
        }

        return freeSpace < LowBytesThreshold || freePercentage < LowPercentThreshold;
    }

    private static bool IsNormalRecovery(long freeSpace, long totalSpace, double freePercentage)
    {
        if (totalSpace > 0 && totalSpace < NormalRecoveryBytesThreshold)
        {
            return freePercentage >= NormalRecoveryPercentThreshold;
        }

        return freeSpace >= NormalRecoveryBytesThreshold && freePercentage >= NormalRecoveryPercentThreshold;
    }

    private void PublishHealthTransitionEvent(
        string path,
        DiskSpaceHealthState oldState,
        DiskSpaceHealthState newState,
        long freeSpace,
        long totalSpace,
        double freePercentage)
    {
        if (newState == DiskSpaceHealthState.Critical)
        {
            _logger.Warn("Disk space on {0} entered CRITICAL state: {1} bytes free", path, freeSpace);
            _eventAggregator.PublishEvent(new DiskSpaceCriticalEvent(path, freeSpace));
        }
        else if (oldState == DiskSpaceHealthState.Normal && newState == DiskSpaceHealthState.Low)
        {
            _logger.Warn("Disk space on {0} entered LOW state: {1} bytes free ({2:P1})", path, freeSpace, freePercentage);
            _eventAggregator.PublishEvent(new DiskSpaceLowEvent(path, freeSpace, totalSpace, freePercentage));
        }
        else if ((oldState == DiskSpaceHealthState.Critical || oldState == DiskSpaceHealthState.Low) && newState == DiskSpaceHealthState.Normal)
        {
            _logger.Info("Disk space on {0} RESTORED to normal: {1} bytes free ({2:P1})", path, freeSpace, freePercentage);
            _eventAggregator.PublishEvent(new DiskSpaceRestoredEvent(path, freeSpace, totalSpace));
        }
    }

    private void AddDriveInfo(
        List<DiskSpaceInfo> result,
        HashSet<string> seen,
        string path,
        string label)
    {
        AddDriveInfoWithDrives(result, seen, path, label, null);
    }

    private void AddDriveInfoWithDrives(
        List<DiskSpaceInfo> result,
        HashSet<string> seen,
        string path,
        string label,
        DriveInfo[] drives)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            drives ??= DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network))
                .ToArray();

            var drive = GetBestMatchingDrive(path, drives);
            if (drive == null || !drive.IsReady)
            {
                return;
            }

            var rootKey = drive.RootDirectory.FullName;
            if (!seen.Add(rootKey))
            {
                return;
            }

            result.Add(new DiskSpaceInfo
            {
                Path = path,
                Label = label,
                FreeSpace = drive.AvailableFreeSpace,
                TotalSpace = drive.TotalSize,
            });
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to get drive info for path {0} ({1})", path, label);
        }
    }

    private static DriveInfo GetBestMatchingDrive(string path, DriveInfo[] drives)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            fullPath = path;
        }

        var normalizedFullPath = fullPath;
        if (!normalizedFullPath.EndsWith(Path.DirectorySeparatorChar.ToString()) && normalizedFullPath != "/")
        {
            normalizedFullPath += Path.DirectorySeparatorChar;
        }

        DriveInfo bestMatch = null;
        var longestMatchLength = -1;

        if (drives != null)
        {
            foreach (var drive in drives)
            {
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    var mountPath = drive.RootDirectory.FullName;
                    var normalizedMountPath = mountPath;
                    if (!normalizedMountPath.EndsWith(Path.DirectorySeparatorChar.ToString()) && normalizedMountPath != "/")
                    {
                        normalizedMountPath += Path.DirectorySeparatorChar;
                    }

                    if (normalizedFullPath.StartsWith(normalizedMountPath, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.Equals(drive.Name.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    {
                        if (normalizedMountPath.Length > longestMatchLength)
                        {
                            longestMatchLength = normalizedMountPath.Length;
                            bestMatch = drive;
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible virtual filesystem mounts
                }
            }
        }

        if (bestMatch != null)
        {
            return bestMatch;
        }

        try
        {
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                return new DriveInfo(root);
            }
        }
        catch
        {
        }

        return null;
    }
}
