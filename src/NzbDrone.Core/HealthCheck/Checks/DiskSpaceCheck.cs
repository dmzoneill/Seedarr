using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.HealthCheck.Checks;

public class DiskSpaceCheck : IHealthCheck
{
    private const long MinFreeBytes = 500 * 1024 * 1024;

    private readonly IAppFolderInfo _appFolderInfo;
    private readonly IConfigService _configService;
    private readonly Func<string, long?> _getFreeSpaceOverride;
    private readonly Func<string, string> _getPathRootOverride;
    private readonly Logger _logger;

    public DiskSpaceCheck(
        IAppFolderInfo appFolderInfo,
        IConfigService configService = null,
        Func<string, long?> getFreeSpaceOverride = null,
        Func<string, string> getPathRootOverride = null)
    {
        _appFolderInfo = appFolderInfo;
        _configService = configService;
        _getFreeSpaceOverride = getFreeSpaceOverride;
        _getPathRootOverride = getPathRootOverride;
        _logger = LogManager.GetCurrentClassLogger();
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

            if (!string.IsNullOrWhiteSpace(defaultSavePath))
            {
                if (_getFreeSpaceOverride != null || Directory.Exists(defaultSavePath) || DriveExistsForPath(defaultSavePath))
                {
                    pathsToCheck.Add((defaultSavePath, "download volume"));
                }
            }

            var watchFolderPath = _configService?.WatchFolderPath;
            if (!string.IsNullOrWhiteSpace(watchFolderPath))
            {
                if (_getFreeSpaceOverride != null || Directory.Exists(watchFolderPath) || DriveExistsForPath(watchFolderPath))
                {
                    pathsToCheck.Add((watchFolderPath, "watch folder volume"));
                }
            }

            if (pathsToCheck.Count == 0)
            {
                return HealthCheckResult.Warning("DiskSpace", "Unable to determine available disk space");
            }

            var volumeGroups = pathsToCheck
                .GroupBy(p => GetVolumeRoot(p.Path), StringComparer.OrdinalIgnoreCase)
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

                    if (isDownload)
                    {
                        warnings.Add($"Low disk space on download volume ({volumeRoot}): {freeMb} MB remaining");
                    }
                    else if (isWatchFolder)
                    {
                        warnings.Add($"Low disk space on watch folder volume ({volumeRoot}): {freeMb} MB remaining");
                    }
                    else if (isAppData)
                    {
                        errors.Add($"Low disk space: {freeMb} MB remaining on {volumeRoot}");
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

    private static bool DriveExistsForPath(string path)
    {
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

    private string GetVolumeRoot(string path)
    {
        if (_getPathRootOverride != null)
        {
            return _getPathRootOverride(path);
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
