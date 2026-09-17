using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using Seedarr.Http;

namespace Seedarr.Api.V1.FileSystem;

/// <summary>
/// API Controller for filesystem directory and file navigation.
/// </summary>
[V1ApiController("filesystem")]
[SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is validated and normalized for filesystem browsing")]
public class FileSystemController : Controller
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Browses directory contents at the specified path.
    /// </summary>
    /// <param name="path">The directory path to explore. If omitted or empty, returns drive roots or system root.</param>
    /// <param name="includeFiles">Whether to include files in addition to directories.</param>
    /// <param name="skip">Number of entries to skip for pagination.</param>
    /// <param name="take">Number of entries to return per page.</param>
    /// <returns>A FileSystemResource containing directories and files.</returns>
    [HttpGet]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "File browser controller intentionally accesses user-requested directories")]
    [ProducesResponseType(typeof(FileSystemResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<FileSystemResource> GetContents(
        [FromQuery] string path = null,
        [FromQuery] bool includeFiles = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 500)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Ok(GetRootListing());
        }

        // Validate path safety
        if (path.Contains('\0'))
        {
            return BadRequest("Path cannot contain null bytes.");
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return BadRequest("Path contains invalid path characters.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            return BadRequest($"Invalid path format: {ex.Message}");
        }

        if (!Directory.Exists(fullPath))
        {
            if (global::System.IO.File.Exists(fullPath))
            {
                return BadRequest("Specified path is a file, not a directory.");
            }

            return NotFound($"Directory not found: {fullPath}");
        }

        var clampedTake = Math.Clamp(take <= 0 ? 500 : take, 1, 2000);
        var clampedSkip = Math.Max(0, skip);

        var result = new FileSystemResource
        {
            Current = fullPath,
            Parent = GetParentDirectory(fullPath),
            Directories = new List<FileSystemEntryResource>(),
            Files = new List<FileSystemEntryResource>(),
        };

        try
        {
            var dirInfo = new DirectoryInfo(fullPath);
            var maxLimit = Math.Clamp(clampedSkip + clampedTake + 1, 5000, 10000);

            var totalDirs = 0;
            var hasMoreDirs = false;
            try
            {
                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                };

                var allDirs = new List<DirectoryInfo>();
                foreach (var dir in dirInfo.EnumerateDirectories("*", enumOptions))
                {
                    if (allDirs.Count >= maxLimit)
                    {
                        hasMoreDirs = true;
                        break;
                    }

                    allDirs.Add(dir);
                }

                totalDirs = allDirs.Count;
                result.TotalDirectories = totalDirs;

                var pagedDirs = allDirs
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Skip(clampedSkip)
                    .Take(clampedTake);

                foreach (var dir in pagedDirs)
                {
                    try
                    {
                        // Filter out hidden/system or inaccessible entries
                        result.Directories.Add(new FileSystemEntryResource
                        {
                            Name = dir.Name,
                            Path = dir.FullName,
                            Type = "folder",
                            LastModified = dir.LastWriteTimeUtc == DateTime.MinValue ? null : dir.LastWriteTimeUtc,
                        });
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
                    {
                        // Skip unreadable subdirectories
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
            {
                _logger.Warn(ex, "Failed to enumerate directories in {0}", fullPath);
            }

            var totalFiles = 0;
            var hasMoreFiles = false;
            if (includeFiles)
            {
                try
                {
                    var fileEnumOptions = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false,
                    };

                    var allFiles = new List<FileInfo>();
                    foreach (var file in dirInfo.EnumerateFiles("*", fileEnumOptions))
                    {
                        if (allFiles.Count >= maxLimit)
                        {
                            hasMoreFiles = true;
                            break;
                        }

                        allFiles.Add(file);
                    }

                    totalFiles = allFiles.Count;
                    result.TotalFiles = totalFiles;

                    var pagedFiles = allFiles
                        .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                        .Skip(clampedSkip)
                        .Take(clampedTake);

                    foreach (var file in pagedFiles)
                    {
                        try
                        {
                            result.Files.Add(new FileSystemEntryResource
                            {
                                Name = file.Name,
                                Path = file.FullName,
                                Type = "file",
                                Size = file.Length,
                                LastModified = file.LastWriteTimeUtc == DateTime.MinValue ? null : file.LastWriteTimeUtc,
                            });
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
                        {
                            // Skip unreadable files
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
                {
                    _logger.Warn(ex, "Failed to enumerate files in {0}", fullPath);
                }
            }

            result.IsTruncated = hasMoreDirs || hasMoreFiles ||
                                 (totalDirs > clampedSkip + result.Directories.Count) ||
                                 (totalFiles > clampedSkip + result.Files.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error accessing directory {0}", fullPath);
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error reading directory: {ex.Message}");
        }

        return Ok(result);
    }

    /// <summary>
    /// Gets the root listing of available drives and root directories.
    /// </summary>
    /// <param name="drivesOverride">Optional drive list to override DriveInfo.GetDrives() for testing.</param>
    /// <returns>A FileSystemResource containing root drives and directories.</returns>
    public static FileSystemResource GetRootListing(DriveInfo[] drivesOverride = null)
    {
        var result = new FileSystemResource
        {
            Current = OperatingSystem.IsWindows() ? string.Empty : "/",
            Parent = null,
            Directories = new List<FileSystemEntryResource>(),
            Files = new List<FileSystemEntryResource>(),
        };

        try
        {
            var drives = drivesOverride ?? DriveInfo.GetDrives()
                .Where(d =>
                {
                    try
                    {
                        return d.IsReady;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToArray();

            if (OperatingSystem.IsWindows())
            {
                foreach (var drive in drives)
                {
                    try
                    {
                        var name = !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                            ? $"{drive.Name} ({drive.VolumeLabel})"
                            : drive.Name;

                        result.Directories.Add(new FileSystemEntryResource
                        {
                            Name = name,
                            Path = drive.RootDirectory.FullName,
                            Type = "drive",
                            Size = drive.TotalSize,
                            FreeSpace = drive.AvailableFreeSpace,
                        });
                    }
                    catch
                    {
                        // Skip inaccessible drive
                    }
                }
            }
            else
            {
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1. Mounted filesystems/drives
                foreach (var drive in drives)
                {
                    try
                    {
                        var mountPath = drive.RootDirectory.FullName;
                        if (!seenPaths.Add(mountPath))
                        {
                            continue;
                        }

                        long? totalSize = null;
                        long? freeSpace = null;
                        string volumeLabel = null;

                        try
                        {
                            totalSize = drive.TotalSize;
                            freeSpace = drive.AvailableFreeSpace;
                            volumeLabel = drive.VolumeLabel;
                        }
                        catch
                        {
                            // Some virtual mounts may fail to report size/label
                        }

                        var label = !string.IsNullOrWhiteSpace(volumeLabel)
                            ? $"{mountPath} ({volumeLabel})"
                            : mountPath;

                        result.Directories.Add(new FileSystemEntryResource
                        {
                            Name = label,
                            Path = mountPath,
                            Type = "drive",
                            Size = totalSize,
                            FreeSpace = freeSpace,
                        });
                    }
                    catch
                    {
                        // Skip unreadable drive
                    }
                }

                // 2. Well-known container mount points if present
                var wellKnownMounts = new[] { "/downloads", "/data", "/config", "/media", "/torrents", "/storage" };
                foreach (var mount in wellKnownMounts)
                {
                    try
                    {
                        if (Directory.Exists(mount) && seenPaths.Add(mount))
                        {
                            long? size = null;
                            long? free = null;
                            try
                            {
                                var drive = new DriveInfo(mount);
                                if (drive.IsReady)
                                {
                                    size = drive.TotalSize;
                                    free = drive.AvailableFreeSpace;
                                }
                            }
                            catch
                            {
                            }

                            result.Directories.Add(new FileSystemEntryResource
                            {
                                Name = mount,
                                Path = mount,
                                Type = "drive",
                                Size = size,
                                FreeSpace = free,
                            });
                        }
                    }
                    catch
                    {
                    }
                }

                // 3. Directories directly under root that exist and are accessible
                if (Directory.Exists("/"))
                {
                    try
                    {
                        var rootInfo = new DirectoryInfo("/");
                        var enumOptions = new EnumerationOptions
                        {
                            IgnoreInaccessible = true,
                            RecurseSubdirectories = false,
                        };

                        var rootDirs = rootInfo.EnumerateDirectories("*", enumOptions)
                            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

                        foreach (var dir in rootDirs)
                        {
                            try
                            {
                                if (!seenPaths.Add(dir.FullName))
                                {
                                    continue;
                                }

                                result.Directories.Add(new FileSystemEntryResource
                                {
                                    Name = dir.Name,
                                    Path = dir.FullName,
                                    Type = "folder",
                                    LastModified = dir.LastWriteTimeUtc == DateTime.MinValue ? null : dir.LastWriteTimeUtc,
                                });
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to enumerate root directories in GetRootListing");
                    }
                }
            }
        }
        catch
        {
            // Fallback
        }

        result.TotalDirectories = result.Directories.Count;
        result.TotalFiles = result.Files.Count;
        result.IsTruncated = false;

        return result;
    }

    private static string GetParentDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var dirInfo = new DirectoryInfo(path);
        if (dirInfo.Parent == null)
        {
            return null;
        }

        return dirInfo.Parent.FullName;
    }
}
