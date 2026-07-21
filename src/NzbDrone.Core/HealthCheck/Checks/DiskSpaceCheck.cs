using System;
using System.IO;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.HealthCheck.Checks;

public class DiskSpaceCheck : IHealthCheck
{
    private const long MinFreeBytes = 500 * 1024 * 1024;

    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;

    public DiskSpaceCheck(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public HealthCheckResult Check()
    {
        try
        {
            var appDataPath = _appFolderInfo.AppDataFolder;
            var driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(appDataPath)) ?? "/");

            if (driveInfo.AvailableFreeSpace < MinFreeBytes)
            {
                var freeMb = driveInfo.AvailableFreeSpace / (1024 * 1024);
                return HealthCheckResult.Error("DiskSpace",
                    $"Low disk space: {freeMb} MB remaining on {driveInfo.Name}");
            }

            return HealthCheckResult.Ok("DiskSpace");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to check disk space for app data folder");
            return HealthCheckResult.Warning("DiskSpace", "Unable to determine available disk space");
        }
    }
}
