using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using NLog;

namespace NzbDrone.Common.Disk;

public class DiskProvider : IDiskProvider
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly Func<string, bool> DirExists = (Func<string, bool>)Delegate.CreateDelegate(
        typeof(Func<string, bool>),
        typeof(Directory).GetMethod(nameof(Directory.Exists), new[] { typeof(string) })!);

    /// <summary>
    /// Optional drive enumerator for testing. When null, <see cref="DriveInfo.GetDrives"/> is used.
    /// </summary>
    public Func<DriveInfo[]> DrivesProvider { get; set; }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is used to query filesystem drive space")]
    public long? GetAvailableFreeSpace(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);

            if (OperatingSystem.IsWindows())
            {
                var volumePath = WindowsVolumeHelper.GetVolumePathForFile(fullPath);
                if (!string.IsNullOrEmpty(volumePath))
                {
                    var volumeDrive = new DriveInfo(volumePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    return ReadAvailableFreeSpace(volumeDrive, path);
                }
            }

            var drives = DrivesProvider?.Invoke() ?? DriveInfo.GetDrives();
            var bestMatch = SelectLongestMatchingDrive(fullPath, drives);
            if (bestMatch != null)
            {
                return ReadAvailableFreeSpace(bestMatch, path);
            }

            if (!OperatingSystem.IsWindows())
            {
                Logger.Warn("Failed to query available free space for path: {0} (no matching mount)", path);
                return 0;
            }

            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            var drive = new DriveInfo(root);
            return ReadAvailableFreeSpace(drive, path);
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to query available free space for path: {0}", path);
            return null;
        }
    }

    private static long? ReadAvailableFreeSpace(DriveInfo drive, string pathForLog)
    {
        if (drive == null)
        {
            Logger.Warn("Failed to query available free space for path: {0} (no matching drive)", pathForLog);
            return null;
        }

        try
        {
            if (!drive.IsReady)
            {
                Logger.Warn("Drive '{0}' is not ready when querying free space for path: {1}", drive.Name, pathForLog);
                return null;
            }

            return drive.AvailableFreeSpace;
        }
        catch (IOException ex)
        {
            Logger.Warn(ex, "I/O error querying free space on drive '{0}' for path: {1}", drive.Name, pathForLog);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warn(ex, "Access denied querying free space on drive '{0}' for path: {1}", drive.Name, pathForLog);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to query free space on drive '{0}' for path: {1}", drive.Name, pathForLog);
            return null;
        }
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is used to verify write permissions")]
    public bool CheckFolderWritable(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (PathSanitizer.ContainsPathTraversal(path) || !PathSanitizer.IsValidPath(path))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(path);

            if (PathSanitizer.IsBlockedPath(fullPath))
            {
                return false;
            }

            if (!DirExists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            var sentinel = Path.Combine(fullPath, $".seedarr_perm_check_{Guid.NewGuid():N}.tmp");
            using (var fs = new FileStream(sentinel, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.WriteByte(0x42);
            }

            if (File.Exists(sentinel))
            {
                File.Delete(sentinel);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsValidFileName(string name)
    {
        return PathSanitizer.IsValidFileName(name);
    }

    public string SanitizeFileName(string name)
    {
        return PathSanitizer.SanitizeFileName(name);
    }

    public bool IsValidPath(string path)
    {
        return PathSanitizer.IsValidPath(path);
    }

    public bool ContainsPathTraversal(string path)
    {
        return PathSanitizer.ContainsPathTraversal(path);
    }

    /// <summary>
    /// Mount prefix matching for non-Windows drive resolution. Paths may differ in case from
    /// <see cref="DriveInfo.Name"/> when users type save paths on case-sensitive filesystems.
    /// </summary>
    private static readonly StringComparison MountPrefixComparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Finds the longest mount point prefix for a filesystem path.
    /// </summary>
    public static string GetLongestMatchingMountPoint(string fullPath, IEnumerable<string> mountPoints)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || mountPoints == null)
        {
            return null;
        }

        var normalizedFullPath = fullPath.Replace('\\', '/');
        if (!normalizedFullPath.EndsWith('/') && normalizedFullPath != "/")
        {
            normalizedFullPath += '/';
        }

        string bestMatch = null;
        var longestMatchLength = -1;

        foreach (var mountPoint in mountPoints)
        {
            if (string.IsNullOrWhiteSpace(mountPoint))
            {
                continue;
            }

            var mountPath = mountPoint.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(mountPath))
            {
                mountPath = "/";
            }

            var normalizedMountPath = mountPath == "/" ? "/" : mountPath + "/";
            if (normalizedFullPath.StartsWith(normalizedMountPath, MountPrefixComparison) ||
                fullPath.Equals(mountPath, MountPrefixComparison) ||
                fullPath.Equals(mountPoint, MountPrefixComparison))
            {
                if (mountPath.Length > longestMatchLength)
                {
                    longestMatchLength = mountPath.Length;
                    bestMatch = mountPoint.TrimEnd('\\', '/');
                    if (string.IsNullOrEmpty(bestMatch))
                    {
                        bestMatch = mountPoint;
                    }
                }
            }
        }

        return bestMatch;
    }

    internal static DriveInfo SelectLongestMatchingDrive(string fullPath, DriveInfo[] drives)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || drives == null || drives.Length == 0)
        {
            return null;
        }

        var mountPoints = drives.Where(d => d != null).Select(d => d.Name).ToArray();
        var bestMount = GetLongestMatchingMountPoint(fullPath, mountPoints);
        if (bestMount == null)
        {
            return null;
        }

        foreach (var drive in drives)
        {
            if (drive == null)
            {
                continue;
            }

            try
            {
                var mountPath = drive.Name.Replace('\\', '/').TrimEnd('/');
                if (string.IsNullOrEmpty(mountPath))
                {
                    mountPath = "/";
                }

                if (string.Equals(mountPath, bestMount, MountPrefixComparison))
                {
                    return drive;
                }
            }
            catch
            {
                // Ignore inaccessible virtual filesystem mounts.
            }
        }

        return null;
    }
}
