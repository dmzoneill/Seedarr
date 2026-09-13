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

        AddDriveInfo(result, seen, _appFolderInfo.AppDataFolder, "AppData", drives);
        AddDriveInfo(result, seen, _appFolderInfo.StartUpFolder, "Startup", drives);

        foreach (var drive in drives)
        {
            try
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
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get space for drive {0}", drive.Name);
            }
        }

        return result;
    }

    private void AddDriveInfo(
        List<DiskSpaceInfo> result,
        HashSet<string> seen,
        string path,
        string label,
        DriveInfo[] drives = null)
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
