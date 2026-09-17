using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace NzbDrone.Common.Disk;

public class DiskProvider : IDiskProvider
{
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

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var sentinel = Path.Combine(path, $".seedarr_perm_check_{Guid.NewGuid():N}.tmp");
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
}
