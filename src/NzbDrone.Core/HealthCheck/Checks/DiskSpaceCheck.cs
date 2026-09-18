using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;

namespace NzbDrone.Core.HealthCheck.Checks;

public class DiskSpaceCheck : IHealthCheck
{
    private const long MinFreeBytes = 500 * 1024 * 1024;

    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IConfigService _configService;
    private readonly IDiskSpaceService _diskSpaceService;
    private readonly Func<string, long?> _getFreeSpaceOverride;
    private readonly Func<string, string> _getPathRootOverride;
    private readonly Logger _logger;

    public DiskSpaceCheck(
        IAppFolderInfo appFolderInfo,
        IConfigService configService = null,
        IDiskSpaceService diskSpaceService = null,
        Func<string, long?> getFreeSpaceOverride = null,
        Func<string, string> getPathRootOverride = null)
    {
        _appFolderInfo = appFolderInfo;
        _configService = configService;
        _diskSpaceService = diskSpaceService;
        _getFreeSpaceOverride = getFreeSpaceOverride;
        _getPathRootOverride = getPathRootOverride;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public DiskSpaceCheck(
        IAppFolderInfo appFolderInfo,
        IConfigService configService,
        Func<string, long?> getFreeSpaceOverride,
        Func<string, string> getPathRootOverride = null)
        : this(appFolderInfo, configService, null, getFreeSpaceOverride, getPathRootOverride)
    {
    }

    public HealthCheckResult Check()
    {
        try
        {
            var pathsToCheck = new List<(string Path, string VolumeRole)>();

            if (_appFolderInfo != null && !string.IsNullOrWhiteSpace(_appFolderInfo.AppDataFolder))
            {
                pathsToCheck.Add((_appFolderInfo.AppDataFolder, "app data volume"));
            }

            var defaultSavePath = _configService?.DefaultSavePath;
            if (string.IsNullOrWhiteSpace(defaultSavePath))
            {
                defaultSavePath = _configService?.TorrentSaveDirectory;
            }

            List<DiskSpaceInfo> diskSpaces = null;
            if (_diskSpaceService != null)
            {
                try
                {
                    diskSpaces = _diskSpaceService.GetDiskSpace();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to retrieve disk spaces from IDiskSpaceService");
                }
            }

            if (!string.IsNullOrWhiteSpace(defaultSavePath))
            {
                if (_getFreeSpaceOverride != null || _diskSpaceService != null || Directory.Exists(defaultSavePath) || DriveExistsForPath(defaultSavePath, diskSpaces))
                {
                    pathsToCheck.Add((defaultSavePath, "download volume"));
                }
            }

            var watchFolderPath = _configService?.WatchFolderPath;
            if (!string.IsNullOrWhiteSpace(watchFolderPath))
            {
                if (_getFreeSpaceOverride != null || _diskSpaceService != null || Directory.Exists(watchFolderPath) || DriveExistsForPath(watchFolderPath, diskSpaces))
                {
                    pathsToCheck.Add((watchFolderPath, "watch folder volume"));
                }
            }

            if (diskSpaces != null)
            {
                foreach (var disk in diskSpaces)
                {
                    if (disk == null || string.IsNullOrWhiteSpace(disk.Path))
                    {
                        continue;
                    }

                    if (pathsToCheck.Any(p => string.Equals(p.Path, disk.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    string role;
                    if (string.Equals(disk.Label, "AppData", StringComparison.OrdinalIgnoreCase))
                    {
                        role = "app data volume";
                    }
                    else if (string.Equals(disk.Label, "Startup", StringComparison.OrdinalIgnoreCase))
                    {
                        role = "app data volume";
                    }
                    else if (!string.Equals(disk.Label, "Root Drive", StringComparison.OrdinalIgnoreCase) &&
                        !disk.Path.Equals("/") &&
                        !disk.Path.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                    {
                        role = "download volume";
                    }
                    else
                    {
                        role = "system volume";
                    }

                    pathsToCheck.Add((disk.Path, role));
                }
            }

            if (pathsToCheck.Count == 0)
            {
                return HealthCheckResult.Warning("DiskSpace", "Unable to determine available disk space");
            }

            var volumeGroups = pathsToCheck
                .GroupBy(p => GetVolumeRoot(p.Path, diskSpaces), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var errors = new List<string>();
            var warnings = new List<string>();

            foreach (var group in volumeGroups)
            {
                var volumeRoot = group.Key;
                long? freeBytesNullable = null;

                try
                {
                    if (_getFreeSpaceOverride != null)
                    {
                        freeBytesNullable = _getFreeSpaceOverride(volumeRoot);
                    }
                    else if (_diskSpaceService != null)
                    {
                        var diskInfo = GetDiskInfoForPath(volumeRoot, diskSpaces);
                        if (diskInfo != null)
                        {
                            freeBytesNullable = diskInfo.FreeSpace;
                        }
                        else
                        {
                            var driveInfo = new DriveInfo(volumeRoot);
                            freeBytesNullable = driveInfo.AvailableFreeSpace;
                        }
                    }
                    else
                    {
                        var driveInfo = new DriveInfo(volumeRoot);
                        freeBytesNullable = driveInfo.AvailableFreeSpace;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to check disk space for volume {0}", volumeRoot);
                    continue;
                }

                if (!freeBytesNullable.HasValue)
                {
                    continue;
                }

                var freeBytes = freeBytesNullable.Value;
                if (freeBytes < MinFreeBytes)
                {
                    var freeMb = freeBytes / (1024 * 1024);
                    var isAppData = group.Any(x => x.VolumeRole == "app data volume");
                    var isDownload = group.Any(x => x.VolumeRole == "download volume");
                    var isWatchFolder = group.Any(x => x.VolumeRole == "watch folder volume");

                    if (isAppData)
                    {
                        errors.Add($"Low disk space: {freeMb} MB remaining on {volumeRoot}");
                    }
                    else if (isDownload)
                    {
                        warnings.Add($"Low disk space on download volume ({volumeRoot}): {freeMb} MB remaining");
                    }
                    else if (isWatchFolder)
                    {
                        warnings.Add($"Low disk space on watch folder volume ({volumeRoot}): {freeMb} MB remaining");
                    }
                    else
                    {
                        warnings.Add($"Low disk space on volume ({volumeRoot}): {freeMb} MB remaining");
                    }
                }
            }

            if (errors.Count > 0)
            {
                return HealthCheckResult.Error("DiskSpace", string.Join("; ", errors));
            }

            if (warnings.Count > 0)
            {
                return HealthCheckResult.Warning("DiskSpace", string.Join("; ", warnings));
            }

            return HealthCheckResult.Ok("DiskSpace");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to check disk space");
            return HealthCheckResult.Warning("DiskSpace", "Unable to determine available disk space");
        }
    }

    private DiskSpaceInfo GetDiskInfoForPath(string path, List<DiskSpaceInfo> diskSpaces)
    {
        if (_diskSpaceService != null)
        {
            try
            {
                var info = _diskSpaceService.GetDiskSpaceForPath(path);
                if (info != null)
                {
                    return info;
                }
            }
            catch
            {
            }
        }

        return MatchDiskForPath(path, diskSpaces);
    }

    private static DiskSpaceInfo MatchDiskForPath(string path, IEnumerable<DiskSpaceInfo> diskSpaces)
    {
        if (string.IsNullOrWhiteSpace(path) || diskSpaces == null)
        {
            return null;
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

        return bestMatch ?? diskSpaces.FirstOrDefault(d => d.Path == "/" || d.Path.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) ?? diskSpaces.FirstOrDefault();
    }

    private static bool DriveExistsForPath(string path, List<DiskSpaceInfo> diskSpaces = null)
    {
        if (diskSpaces != null && MatchDiskForPath(path, diskSpaces) != null)
        {
            return true;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private string GetVolumeRoot(string path, List<DiskSpaceInfo> diskSpaces = null)
    {
        if (_getPathRootOverride != null)
        {
            return _getPathRootOverride(path);
        }

        if (_diskSpaceService != null)
        {
            var match = GetDiskInfoForPath(path, diskSpaces);
            if (match != null && !string.IsNullOrWhiteSpace(match.Path))
            {
                return match.Path;
            }
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                return root;
            }
        }
        catch
        {
        }

        return "/";
    }
}
