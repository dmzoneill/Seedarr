using System;
using System.Collections.Generic;
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
public class FileSystemController : Controller
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Browses directory contents at the specified path.
    /// </summary>
    /// <param name="path">The directory path to explore. If omitted or empty, returns drive roots or system root.</param>
    /// <param name="includeFiles">Whether to include files in addition to directories.</param>
    /// <returns>A FileSystemResource containing directories and files.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(FileSystemResource), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<FileSystemResource> GetContents([FromQuery] string path = null, [FromQuery] bool includeFiles = false)
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
            if (File.Exists(fullPath))
            {
                return BadRequest("Specified path is a file, not a directory.");
            }

            return NotFound($"Directory not found: {fullPath}");
        }

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

            try
            {
                var directories = dirInfo.EnumerateDirectories()
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var dir in directories)
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

            if (includeFiles)
            {
                try
                {
                    var files = dirInfo.EnumerateFiles()
                        .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

                    foreach (var file in files)
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
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error accessing directory {0}", fullPath);
            return StatusCode(StatusCodes.Status500InternalServerError, $"Error reading directory: {ex.Message}");
        }

        return Ok(result);
    }

    private static FileSystemResource GetRootListing()
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
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .ToList();

            if (OperatingSystem.IsWindows())
            {
                foreach (var drive in drives)
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
                    });
                }
            }
            else
            {
                // On Unix-like systems, if root is requested or default
                if (Directory.Exists("/"))
                {
                    var rootInfo = new DirectoryInfo("/");
                    foreach (var dir in rootInfo.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
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
            }
        }
        catch
        {
            // Fallback
        }

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
