using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.DiskSpace;

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
}

/// <summary>
/// Default implementation of <see cref="IDiskSpaceService"/>.
/// </summary>
public class DiskSpaceService : IDiskSpaceService
{
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    internal TimeSpan DriveTimeout { get; set; } = TimeSpan.FromSeconds(3);

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
    public List<DiskSpaceInfo> GetDiskSpace()
    {
        var result = new List<DiskSpaceInfo>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allDrives = GetAllDrivesSafe();

        var readyFixedOrNetworkDrives = allDrives
            .Where(d =>
            {
                try
                {
                    return d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Network;
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();

        // 1. All fixed and network system drives (ensures the root mount '/' and physical storage drives are reported)
        foreach (var drive in readyFixedOrNetworkDrives)
        {
            try
            {
                var rootPath = drive.RootDirectory.FullName;
                if (!seenRoots.Add(rootPath))
                {
                    continue;
                }

                if (!TryGetDriveStats(drive, out var stats))
                {
                    continue;
                }

                var label = !string.IsNullOrWhiteSpace(stats.VolumeLabel)
                    ? stats.VolumeLabel
                    : (rootPath == "/" ? "Root Drive" : rootPath);

                var info = new DiskSpaceInfo
                {
                    Path = rootPath,
                    Label = label,
                    FreeSpace = stats.FreeSpace,
                    TotalSpace = stats.TotalSpace,
                };
                result.Add(info);

                PublishDiskSpaceEvents(info);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get space for drive {0}", drive.Name);
            }
        }

        // 2. AppData and Startup folders (appended separately rather than masking root drive)
        var appFolderSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDriveInfoWithDrives(result, appFolderSeen, _appFolderInfo?.AppDataFolder, "AppData", allDrives);
        AddDriveInfoWithDrives(result, appFolderSeen, _appFolderInfo?.StartUpFolder, "Startup", allDrives);

        // 3. Configured category save paths (ensures mounted torrent volumes and bind/FUSE mounts are inspected and reported)
        var categorySeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categories = GetCategories();
        foreach (var category in categories)
        {
            if (!string.IsNullOrWhiteSpace(category.SavePath))
            {
                var label = !string.IsNullOrWhiteSpace(category.Name) ? category.Name : "Category";
                AddDriveInfoWithDrives(result, categorySeen, category.SavePath, label, allDrives);
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

    private void PublishDiskSpaceEvents(DiskSpaceInfo info)
    {
        if (_eventAggregator != null && info.TotalSpace > 0)
        {
            var freePercentage = (double)info.FreeSpace / info.TotalSpace;
            if (info.FreeSpace < 1024L * 1024 * 1024)
            {
                _eventAggregator.PublishEvent(new DiskSpaceCriticalEvent(info.Path, info.FreeSpace));
            }
            else if (info.FreeSpace < 5L * 1024 * 1024 * 1024 || freePercentage < 0.05)
            {
                _eventAggregator.PublishEvent(new DiskSpaceLowEvent(info.Path, info.FreeSpace, info.TotalSpace, freePercentage));
            }
        }
    }

    private DriveInfo[] GetAllDrivesSafe()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d =>
                {
                    try
                    {
                        return d.IsReady;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to enumerate drives");
            return Array.Empty<DriveInfo>();
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
            drives ??= GetAllDrivesSafe();

            var drive = GetBestMatchingDrive(path, drives);
            if (drive == null)
            {
                return;
            }

            var rootKey = drive.RootDirectory.FullName;
            if (!seen.Add(rootKey))
            {
                return;
            }

            if (!TryGetDriveStats(drive, out var stats))
            {
                return;
            }

            var info = new DiskSpaceInfo
            {
                Path = path,
                Label = label,
                FreeSpace = stats.FreeSpace,
                TotalSpace = stats.TotalSpace,
            };
            result.Add(info);

            PublishDiskSpaceEvents(info);
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
            if (drive.DriveType == DriveType.Network)
            {
                var task = Task.Run(() => ReadDriveStats(drive));
                if (task.Wait(DriveTimeout))
                {
                    var res = task.Result;
                    if (res.HasValue)
                    {
                        stats = res.Value;
                        return true;
                    }

                    return false;
                }

                _logger.Warn("Timed out inspecting network drive {0}", drive.Name);
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

            return new DriveSpaceStats
            {
                FreeSpace = drive.AvailableFreeSpace,
                TotalSpace = drive.TotalSize,
                VolumeLabel = label,
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

        if (bestMatch != null && bestMatch.RootDirectory.FullName != "/")
        {
            return bestMatch;
        }

        try
        {
            var directDrive = new DriveInfo(fullPath);
            if (directDrive.IsReady && (bestMatch == null || directDrive.RootDirectory.FullName.Length > bestMatch.RootDirectory.FullName.Length))
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

    private struct DriveSpaceStats
    {
        public long FreeSpace;
        public long TotalSpace;
        public string VolumeLabel;
    }
}
