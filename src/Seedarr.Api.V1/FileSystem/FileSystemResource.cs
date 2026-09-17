using System;
using System.Collections.Generic;

namespace Seedarr.Api.V1.FileSystem;

/// <summary>
/// Result resource representing directory contents for the filesystem browser.
/// </summary>
public class FileSystemResource
{
    /// <summary>
    /// Gets or sets the parent folder path.
    /// </summary>
    public string Parent { get; set; }

    /// <summary>
    /// Gets or sets the current directory path.
    /// </summary>
    public string Current { get; set; }

    /// <summary>
    /// Gets or sets the list of child directories.
    /// </summary>
    public List<FileSystemEntryResource> Directories { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of files if requested.
    /// </summary>
    public List<FileSystemEntryResource> Files { get; set; } = new();
}

/// <summary>
/// Entry resource representing a file, folder, or drive.
/// </summary>
public class FileSystemEntryResource
{
    /// <summary>
    /// Gets or sets the entry name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the entry absolute path.
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Gets or sets the entry type ("folder", "file", "drive").
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the file size in bytes if applicable.
    /// </summary>
    public long? Size { get; set; }

    /// <summary>
    /// Gets or sets the available free space in bytes if applicable.
    /// </summary>
    public long? FreeSpace { get; set; }

    /// <summary>
    /// Gets or sets the last modified timestamp in UTC.
    /// </summary>
    public DateTime? LastModified { get; set; }
}
