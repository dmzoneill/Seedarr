using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;

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
    private readonly Logger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskSpaceService"/> class.
    /// </summary>
    /// <param name="appFolderInfo">Application folder information.</param>
    public DiskSpaceService(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    /// <inheritdoc/>
    public List<DiskSpaceInfo> GetDiskSpace()
    {
        var result = new List<DiskSpaceInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDriveInfo(result, seen, _appFolderInfo.AppDataFolder, "AppData");
        AddDriveInfo(result, seen, _appFolderInfo.StartUpFolder, "Startup");

        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .ToList();

            foreach (var drive in drives)
            {
                if (seen.Add(drive.RootDirectory.FullName))
                {
                    result.Add(new DiskSpaceInfo
                    {
                        Path = drive.RootDirectory.FullName,
                        Label = drive.VolumeLabel.Length > 0 ? drive.VolumeLabel : drive.RootDirectory.FullName,
                        FreeSpace = drive.AvailableFreeSpace,
                        TotalSpace = drive.TotalSize,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to enumerate fixed drives");
        }

        return result;
    }

    private void AddDriveInfo(
        List<DiskSpaceInfo> result,
        HashSet<string> seen,
        string path,
        string label)
    {
        try
        {
            var root = Path.GetPathRoot(path);

            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            if (!seen.Add(root))
            {
                return;
            }

            var drive = new DriveInfo(root);

            if (drive.IsReady)
            {
                result.Add(new DiskSpaceInfo
                {
                    Path = path,
                    Label = label,
                    FreeSpace = drive.AvailableFreeSpace,
                    TotalSpace = drive.TotalSize,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to get drive info for path {0} ({1})", path, label);
        }
    }
}
