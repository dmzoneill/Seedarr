using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.Torrents;

/// <summary>
/// Preallocates file storage for torrent downloads to avoid filesystem fragmentation and mid-download disk exhaustion.
/// </summary>
public class DiskAllocationService : IDiskAllocationService
{
    private readonly IDiskSpaceService _diskSpaceService;
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    [DllImport("libc", EntryPoint = "posix_fallocate", SetLastError = true)]
    private static extern int PosixFallocate(int fd, long offset, long len);

    public DiskAllocationService(
        IDiskSpaceService diskSpaceService = null,
        IConfigService configService = null)
    {
        _diskSpaceService = diskSpaceService;
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    /// <inheritdoc/>
    public void PreallocateFiles(Torrent torrent, IList<TorrentFile> files, string baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        var mode = _configService?.PreallocationMode ?? PreallocationMode.Sparse;
        if (mode == PreallocationMode.None)
        {
            _logger.Debug("Preallocation mode is None; bypassing disk preallocation for torrent '{0}'", torrent.Name);
            return;
        }

        var targetDirectory = !string.IsNullOrWhiteSpace(baseDirectory)
            ? baseDirectory
            : (!string.IsNullOrWhiteSpace(torrent.SavePath)
                ? torrent.SavePath
                : (_configService?.DefaultSavePath ?? _configService?.TorrentSaveDirectory ?? string.Empty));

        var fileList = files ?? torrent.Files;

        // Check drive free space via IDiskSpaceService
        if (_diskSpaceService != null)
        {
            var diskInfo = _diskSpaceService.GetDiskSpaceForPath(targetDirectory);
            if (diskInfo != null)
            {
                var requiredSpace = torrent.TotalSize > 0
                    ? (torrent.Downloaded > 0 ? Math.Max(0L, torrent.TotalSize - torrent.Downloaded) : torrent.TotalSize)
                    : (fileList != null ? fileList.Where(f => f != null).Sum(f => f.Size) : 0L);

                if (diskInfo.FreeSpace < requiredSpace)
                {
                    _logger.Warn(
                        "Insufficient disk space for preallocation on volume '{0}' for torrent '{1}'. Required: {2} bytes, Available: {3} bytes",
                        diskInfo.Path,
                        torrent.Name,
                        requiredSpace,
                        diskInfo.FreeSpace);

                    throw new InsufficientDiskSpaceException("Insufficient disk space");
                }
            }
        }

        if (fileList == null || fileList.Count == 0)
        {
            return;
        }

        foreach (var file in fileList)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.Path))
            {
                continue;
            }

            var filePath = ResolveFilePath(targetDirectory, file.Path);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            PreallocateFile(filePath, file.Size, mode);
        }
    }

    private void PreallocateFile(string filePath, long size, PreallocationMode mode)
    {
        if (size < 0)
        {
            size = 0;
        }

        using var stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        var allocated = false;
        if (mode == PreallocationMode.Full && OperatingSystem.IsLinux() && size > 0)
        {
            try
            {
                var fd = stream.SafeFileHandle.DangerousGetHandle().ToInt32();
                var ret = PosixFallocate(fd, 0, size);
                if (ret == 0)
                {
                    allocated = true;
                }
                else
                {
                    _logger.Trace("posix_fallocate returned {0} for '{1}', falling back to SetLength", ret, filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Trace(ex, "posix_fallocate failed for '{0}', falling back to SetLength", filePath);
            }
        }

        if (!allocated && stream.Length != size)
        {
            stream.SetLength(size);
        }

        stream.Flush();
    }

    private static string ResolveFilePath(string baseDirectory, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (Path.IsPathRooted(filePath))
        {
            return filePath;
        }

        var normalizedRelative = filePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(baseDirectory) ? normalizedRelative : Path.Combine(baseDirectory, normalizedRelative);
    }
}
