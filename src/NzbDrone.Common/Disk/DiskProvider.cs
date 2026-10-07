using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace NzbDrone.Common.Disk;

public class DiskProvider : IDiskProvider
{
    private static readonly Func<string, bool> DirExists = (Func<string, bool>)Delegate.CreateDelegate(
        typeof(Func<string, bool>),
        typeof(Directory).GetMethod(nameof(Directory.Exists), new[] { typeof(string) })!);

    /// <summary>
    /// Optional drive enumerator for testing. When null, <see cref="DriveInfo.GetDrives"/> is used.
    /// </summary>
    public Func<DriveInfo[]> DrivesProvider { get; set; }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is used to query filesystem drive space")]
    public long GetAvailableFreeSpace(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return 0;
            }

            var fullPath = Path.GetFullPath(path);

            if (!OperatingSystem.IsWindows())
            {
                var drives = DrivesProvider?.Invoke() ?? DriveInfo.GetDrives();
                var bestMatch = SelectLongestMatchingDrive(fullPath, drives);
                if (bestMatch != null)
                {
                    return bestMatch.AvailableFreeSpace;
                }
            }

            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return 0;
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
                    bestMatch = mountPath;
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
