using System.IO;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.HealthCheck.Checks;

public class DiskSpaceCheck : IHealthCheck
{
    private const long MinFreeBytes = 500 * 1024 * 1024;

    private readonly IAppFolderInfo _appFolderInfo;

    public DiskSpaceCheck(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
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
        catch
        {
            return HealthCheckResult.Ok("DiskSpace");
        }
    }
}
