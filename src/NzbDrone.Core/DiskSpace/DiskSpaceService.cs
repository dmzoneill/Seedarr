using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
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
/// Represents parsed mount point information from /proc/mounts.
/// </summary>
public class MountInfo
{
    /// <summary>
    /// Gets or sets the underlying device identifier.
    /// </summary>
    public string Device { get; set; }

    /// <summary>
    /// Gets or sets the mount point path.
    /// </summary>
    public string MountPoint { get; set; }

    /// <summary>
    /// Gets or sets the filesystem type (e.g., ext4, nfs, cifs).
    /// </summary>
    public string FileSystemType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the mount is read-only.
    /// </summary>
    public bool IsReadOnly { get; set; }
}

/// <summary>
/// Raw drive space statistics.
/// </summary>
public struct DriveSpaceStats
{
    /// <summary>
    /// Gets or sets free space in bytes.
    /// </summary>
    public long FreeSpace { get; set; }

    /// <summary>
    /// Gets or sets total space in bytes.
    /// </summary>
    public long TotalSpace { get; set; }

    /// <summary>
    /// Gets or sets volume label.
    /// </summary>
    public string VolumeLabel { get; set; }

    /// <summary>
    /// Gets or sets filesystem type.
    /// </summary>
    public string FileSystemType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the drive is read-only.
    /// </summary>
    public bool IsReadOnly { get; set; }
}

/// <summary>
/// Provides disk space information for relevant locations.
/// </summary>
public interface IDiskSpaceService
{
    /// <summary>
    /// Returns disk space information for all relevant locations.
    /// </summary>
    /// <param name="forceRefresh">Whether to bypass the in-memory TTL cache and query the filesystem immediately.</param>
    /// <returns>A list of disk space information.</returns>
    List<DiskSpaceInfo> GetDiskSpace(bool forceRefresh = false);

    /// <summary>
    /// Evaluates disk space health against critical and low thresholds, publishing health transition events as needed.
    /// </summary>
    /// <param name="diskSpaces">Optional explicit disk space items to evaluate. If null, queries current disk space.</param>
    void CheckDiskSpaceThresholds(IEnumerable<DiskSpaceInfo> diskSpaces = null);

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

    /// <summary>
    /// Gets disk space information for the drive matching the specified path.
    /// </summary>
    /// <param name="path">The filesystem path to match.</param>
    /// <returns>Matching disk space info, or null if not found.</returns>
    DiskSpaceInfo GetDiskSpaceForPath(string path);
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
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, DiskSpaceHealthState> _healthStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();
    private readonly object _cacheLock = new();
    private List<DiskSpaceInfo> _cachedDiskSpace;
    private DateTime _lastCacheTime = DateTime.MinValue;

    /// <summary>
    /// Gets or sets the timeout for probing individual drives.
    /// </summary>
    public TimeSpan DriveTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets the TTL cache duration for disk space queries.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a custom drive provider delegate, primarily for testing.
    /// </summary>
    public Func<DriveInfo[]> DrivesProvider { get; set; }

    /// <summary>
    /// Gets or sets a custom drive stats reader delegate, primarily for testing.
    /// </summary>
    public Func<DriveInfo, DriveSpaceStats?> DriveStatsReader { get; set; }

    /// <summary>
    /// Gets or sets a custom /proc/mounts provider delegate, primarily for testing.
    /// </summary>
    public Func<string> ProcMountsProvider { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskSpaceService"/> class.
    /// </summary>
    /// <param name="appFolderInfo">Application folder information.</param>
    /// <param name="categoryRepository">Repository for querying category storage locations.</param>
    /// <param name="categoryService">Service for querying category storage locations.</param>
    /// <param name="eventAggregator">Event aggregator for publishing disk space threshold events.</param>
    public DiskSpaceService(
        IAppFolderInfo appFolderInfo,
        ICategoryRepository categoryRepository = null,
        ICategoryService categoryService = null,
        IEventAggregator eventAggregator = null)
    {
        _appFolderInfo = appFolderInfo;
        _categoryRepository = categoryRepository;
        _categoryService = categoryService;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskSpaceService"/> class with event aggregator.
    /// </summary>
    /// <param name="appFolderInfo">Application folder information.</param>
    /// <param name="eventAggregator">Event aggregator for publishing disk space threshold events.</param>
    public DiskSpaceService(
        IAppFolderInfo appFolderInfo,
        IEventAggregator eventAggregator)
        : this(appFolderInfo, null, null, eventAggregator)
    {
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
        lock (_cacheLock)
        {
            _cachedDiskSpace = null;
            _lastCacheTime = DateTime.MinValue;
        }
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
        var transitionOccurred = false;

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
    public List<DiskSpaceInfo> GetDiskSpace(bool forceRefresh = false)
    {
        lock (_cacheLock)
        {
            var now = DateTime.UtcNow;
            if (!forceRefresh && _cachedDiskSpace != null && (now - _lastCacheTime) < CacheTtl)
            {
                return _cachedDiskSpace;
            }

            _cachedDiskSpace = QueryDiskSpace();
            _lastCacheTime = now;
            return _cachedDiskSpace;
        }
    }

    /// <inheritdoc/>
    public void CheckDiskSpaceThresholds(IEnumerable<DiskSpaceInfo> diskSpaces = null)
    {
        var items = diskSpaces ?? GetDiskSpace(forceRefresh: true);
        foreach (var info in items)
        {
            if (info.TotalSpace > 0)
            {
                UpdateDiskSpaceHealth(info.Path, info.FreeSpace, info.TotalSpace);
            }
        }
    }

    /// <inheritdoc/>
    public DiskSpaceInfo GetDiskSpaceForPath(string path)
    {
        var diskSpaces = GetDiskSpace();
        if (diskSpaces == null || diskSpaces.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return diskSpaces.FirstOrDefault(d => d.Path == "/" || d.Path.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) ?? diskSpaces[0];
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path).Replace('\\', '/');
        }
        catch
        {
            fullPath = path.Replace('\\', '/');
        }

        if (!fullPath.EndsWith("/"))
        {
            fullPath += "/";
        }

        DiskSpaceInfo bestMatch = null;
        var longestLen = -1;

        foreach (var disk in diskSpaces)
        {
            if (disk == null || string.IsNullOrWhiteSpace(disk.Path))
            {
                continue;
            }

            var diskPath = disk.Path.Replace('\\', '/');
            var checkPath = diskPath.EndsWith("/") ? diskPath : diskPath + "/";

            if (fullPath.StartsWith(checkPath, StringComparison.OrdinalIgnoreCase) ||
                fullPath.Equals(checkPath, StringComparison.OrdinalIgnoreCase))
            {
                if (diskPath.Length > longestLen)
                {
                    longestLen = diskPath.Length;
                    bestMatch = disk;
                }
            }
        }

        return bestMatch ?? diskSpaces.FirstOrDefault(d => d.Path == "/" || d.Path.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) ?? diskSpaces[0];
    }

    private List<DiskSpaceInfo> QueryDiskSpace()
    {
        var result = new List<DiskSpaceInfo>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var systemSeenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allDrives = GetAllDrivesSafe();
        var mounts = ParseMounts(ReadProcMounts());

        // 1. All fixed and network system drives (ensures the root mount '/' and physical storage drives are reported)
        foreach (var drive in allDrives)
        {
            if (drive == null)
            {
                continue;
            }

            try
            {
                var driveType = DriveType.Unknown;
                try
                {
                    driveType = drive.DriveType;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to get drive type for drive {0}, skipping", drive.Name);
                }

                string rootPath;
                try
                {
                    rootPath = drive.RootDirectory.FullName;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to get root directory for drive {0}", drive.Name);
                    continue;
                }

                if (!seenRoots.Add(rootPath))
                {
                    continue;
                }

                var mountInfo = FindMountForPath(rootPath, mounts);
                var isPhysicalOrNetwork = (driveType == DriveType.Fixed || driveType == DriveType.Network) ||
                                          (mountInfo != null && IsPhysicalOrNetworkDevice(mountInfo.Device, mountInfo.FileSystemType));

                if (!isPhysicalOrNetwork)
                {
                    continue;
                }

                if (mountInfo != null && IsPhysicalOrNetworkDevice(mountInfo.Device, mountInfo.FileSystemType))
                {
                    if (!systemSeenDevices.Add(mountInfo.Device))
                    {
                        _logger.Debug("Skipping drive {0} because device {1} is already monitored", rootPath, mountInfo.Device);
                        continue;
                    }
                }

                if (!TryGetDriveStats(drive, out var stats))
                {
                    continue;
                }

                var label = !string.IsNullOrWhiteSpace(stats.VolumeLabel)
                    ? stats.VolumeLabel
                    : (rootPath == "/" ? "Root Drive" : rootPath);

                var fileSystemType = !string.IsNullOrWhiteSpace(mountInfo?.FileSystemType)
                    ? mountInfo.FileSystemType
                    : stats.FileSystemType;

                var isReadOnly = (mountInfo != null && mountInfo.IsReadOnly) || stats.IsReadOnly;

                var info = new DiskSpaceInfo
                {
                    Path = rootPath,
                    Label = label,
                    FreeSpace = stats.FreeSpace,
                    TotalSpace = stats.TotalSpace,
                    FileSystemType = fileSystemType,
                    IsReadOnly = isReadOnly,
                };
                result.Add(info);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get space for drive {0}", drive.Name);
            }
        }

        // 2. AppData and Startup folders (appended separately rather than masking root drive)
        var appFolderSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDriveInfoWithDrives(result, appFolderSeen, null, _appFolderInfo?.AppDataFolder, "AppData", allDrives, mounts);
        AddDriveInfoWithDrives(result, appFolderSeen, null, _appFolderInfo?.StartUpFolder, "Startup", allDrives, mounts);

        // 3. Configured category save paths (ensures mounted torrent volumes and bind/FUSE mounts are inspected and reported)
        var categorySeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categorySeenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = GetCategories();
        foreach (var category in categories)
        {
            if (!string.IsNullOrWhiteSpace(category.SavePath))
            {
                var label = !string.IsNullOrWhiteSpace(category.Name) ? category.Name : "Category";
                AddDriveInfoWithDrives(result, categorySeen, categorySeenDevices, category.SavePath, label, allDrives, mounts);
            }
        }

        return result;
    }

    private IEnumerable<Category> GetCategories()
    {
        if (_categoryRepository != null)
        {
            try
            {
                return _categoryRepository.All() ?? Enumerable.Empty<Category>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to retrieve categories from repository");
            }
        }
        else if (_categoryService != null)
        {
            try
            {
                return _categoryService.GetAll() ?? Enumerable.Empty<Category>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to retrieve categories from service");
            }
        }

        return Enumerable.Empty<Category>();
    }

    private DriveInfo[] GetAllDrivesSafe()
    {
        if (DrivesProvider != null)
        {
            try
            {
                return DrivesProvider() ?? Array.Empty<DriveInfo>();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to enumerate drives from custom provider");
                return Array.Empty<DriveInfo>();
            }
        }

        try
        {
            return DriveInfo.GetDrives() ?? Array.Empty<DriveInfo>();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to enumerate drives");
            return Array.Empty<DriveInfo>();
        }
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
        AddDriveInfoWithDrives(result, seen, null, path, label, null, null);
    }

    private void AddDriveInfoWithDrives(
        List<DiskSpaceInfo> result,
        HashSet<string> seenRoots,
        HashSet<string> seenDevices,
        string path,
        string label,
        DriveInfo[] drives,
        List<MountInfo> mounts)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            drives ??= GetAllDrivesSafe();

            var drive = GetBestMatchingDrive(path, drives);
            if (drive == null)
            {
                return;
            }

            string rootKey;
            try
            {
                rootKey = drive.RootDirectory.FullName;
            }
            catch
            {
                rootKey = path;
            }

            if (!seenRoots.Add(rootKey))
            {
                return;
            }

            var mountInfo = FindMountForPath(path, mounts) ?? FindMountForPath(rootKey, mounts);
            if (seenDevices != null && mountInfo != null && IsPhysicalOrNetworkDevice(mountInfo.Device, mountInfo.FileSystemType))
            {
                if (!seenDevices.Add(mountInfo.Device))
                {
                    _logger.Debug("Skipping path {0} because device {1} is already monitored", path, mountInfo.Device);
                    return;
                }
            }

            if (!TryGetDriveStats(drive, out var stats))
            {
                return;
            }

            var fileSystemType = !string.IsNullOrWhiteSpace(mountInfo?.FileSystemType)
                ? mountInfo.FileSystemType
                : stats.FileSystemType;

            var isReadOnly = (mountInfo != null && mountInfo.IsReadOnly) || stats.IsReadOnly;

            var info = new DiskSpaceInfo
            {
                Path = path,
                Label = label,
                FreeSpace = stats.FreeSpace,
                TotalSpace = stats.TotalSpace,
                FileSystemType = fileSystemType,
                IsReadOnly = isReadOnly,
            };
            result.Add(info);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to get drive info for path {0} ({1})", path, label);
        }
    }

    private bool TryGetDriveStats(DriveInfo drive, out DriveSpaceStats stats)
    {
        stats = default;
        if (drive == null)
        {
            return false;
        }

        try
        {
            if (DriveStatsReader != null)
            {
                var custom = DriveStatsReader(drive);
                if (custom.HasValue)
                {
                    stats = custom.Value;
                    return true;
                }

                return false;
            }

            var direct = ReadDriveStats(drive);
            if (direct.HasValue)
            {
                stats = direct.Value;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to inspect drive {0}", drive.Name);
            return false;
        }
    }

    private static DriveSpaceStats? ReadDriveStats(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return null;
            }

            var label = string.Empty;
            try
            {
                label = drive.VolumeLabel ?? string.Empty;
            }
            catch
            {
            }

            string fsType = null;
            try
            {
                fsType = drive.DriveFormat;
            }
            catch
            {
            }

            return new DriveSpaceStats
            {
                FreeSpace = drive.AvailableFreeSpace,
                TotalSpace = drive.TotalSize,
                VolumeLabel = label,
                FileSystemType = fsType,
                IsReadOnly = false,
            };
        }
        catch
        {
            return null;
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
                    if (drive == null)
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

        if (bestMatch != null && bestMatch.RootDirectory.FullName != "/")
        {
            return bestMatch;
        }

        try
        {
            var directDrive = new DriveInfo(fullPath);
            if (bestMatch == null || directDrive.RootDirectory.FullName.Length > bestMatch.RootDirectory.FullName.Length)
            {
                return directDrive;
            }
        }
        catch
        {
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

    private string ReadProcMounts()
    {
        if (ProcMountsProvider != null)
        {
            try
            {
                return ProcMountsProvider();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to read mounts from custom provider");
                return null;
            }
        }

        try
        {
            if (File.Exists("/proc/mounts"))
            {
                return File.ReadAllText("/proc/mounts");
            }
        }
        catch (Exception ex)
        {
            _logger.Trace(ex, "Unable to read /proc/mounts");
        }

        return null;
    }

    /// <summary>
    /// Parses Linux /proc/mounts content into a list of <see cref="MountInfo"/> objects.
    /// </summary>
    /// <param name="content">Raw /proc/mounts file content.</param>
    /// <returns>A list of parsed mount items.</returns>
    public static List<MountInfo> ParseMounts(string content)
    {
        var mounts = new List<MountInfo>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return mounts;
        }

        using var reader = new StringReader(content);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
            {
                continue;
            }

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            var device = UnescapeMountString(parts[0]);
            var mountPoint = UnescapeMountString(parts[1]);
            var fsType = parts[2];
            var options = parts[3];

            var isReadOnly = options.Split(',').Any(opt => string.Equals(opt, "ro", StringComparison.OrdinalIgnoreCase));

            mounts.Add(new MountInfo
            {
                Device = device,
                MountPoint = mountPoint,
                FileSystemType = fsType,
                IsReadOnly = isReadOnly,
            });
        }

        return mounts;
    }

    private static string UnescapeMountString(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('\\'))
        {
            return value;
        }

        return value
            .Replace("\\040", " ")
            .Replace("\\011", "\t")
            .Replace("\\012", "\n")
            .Replace("\\134", "\\");
    }

    private static MountInfo FindMountForPath(string path, List<MountInfo> mounts)
    {
        if (mounts == null || mounts.Count == 0 || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = path.Replace('\\', '/');
        if (!normalizedPath.EndsWith("/"))
        {
            normalizedPath += "/";
        }

        MountInfo bestMatch = null;
        var bestMatchLen = -1;

        foreach (var mount in mounts)
        {
            var mountPoint = mount.MountPoint.Replace('\\', '/');
            var checkPoint = mountPoint.EndsWith("/") ? mountPoint : mountPoint + "/";

            if (normalizedPath.StartsWith(checkPoint, StringComparison.Ordinal) ||
                normalizedPath.Equals(checkPoint, StringComparison.Ordinal))
            {
                if (mountPoint.Length >= bestMatchLen)
                {
                    bestMatchLen = mountPoint.Length;
                    bestMatch = mount;
                }
            }
        }

        return bestMatch;
    }

    private static bool IsPhysicalOrNetworkDevice(string device, string fsType)
    {
        if (string.IsNullOrWhiteSpace(device))
        {
            return false;
        }

        if (device.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            device.Equals("tmpfs", StringComparison.OrdinalIgnoreCase) ||
            device.Equals("devtmpfs", StringComparison.OrdinalIgnoreCase) ||
            device.Equals("overlay", StringComparison.OrdinalIgnoreCase) ||
            device.Equals("ramfs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(fsType))
        {
            var pseudoFs = new[] { "tmpfs", "devtmpfs", "devpts", "proc", "sysfs", "cgroup", "cgroup2", "pstore", "bpf", "configfs", "selinuxfs", "autofs", "ramfs", "mqueue", "hugetlbfs", "fusectl", "nsfs" };
            if (pseudoFs.Any(p => string.Equals(p, fsType, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var physicalOrNetworkFs = new[] { "ext4", "ext3", "ext2", "xfs", "btrfs", "zfs", "ntfs", "vfat", "fat32", "f2fs", "nfs", "nfs4", "cifs", "smb3", "glusterfs", "ceph", "sshfs" };
            if (physicalOrNetworkFs.Any(n => string.Equals(n, fsType, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (device.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (device.StartsWith("//") || device.StartsWith(@"\\") || device.Contains(':'))
        {
            return true;
        }

        if (device.StartsWith("UUID=", StringComparison.OrdinalIgnoreCase) ||
            device.StartsWith("LABEL=", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
