using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskSpaceService"/> class.
    /// </summary>
    /// <param name="appFolderInfo">Application folder information.</param>
    public DiskSpaceService(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
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
        catch (Exception)
        {
            // If we cannot enumerate drives, return what we have
        }

        return result;
    }

    private static void AddDriveInfo(
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
        catch (Exception)
        {
            // Ignore drives that are inaccessible
        }
    }
}
