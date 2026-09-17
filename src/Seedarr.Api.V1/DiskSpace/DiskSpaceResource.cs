using Seedarr.Http.REST;

namespace Seedarr.Api.V1.DiskSpace;

/// <summary>
/// API resource representing disk space for a single location.
/// </summary>
public class DiskSpaceResource : RestResource
{
    /// <summary>
    /// Gets or sets the filesystem path.
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Gets or sets the display label.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// Gets or sets the free space in bytes.
    /// </summary>
    public long FreeSpace { get; set; }

    /// <summary>
    /// Gets or sets the total space in bytes.
    /// </summary>
    public long TotalSpace { get; set; }

    /// <summary>
    /// Gets or sets the filesystem type (e.g. ext4, zfs, nfs, btrfs, ntfs).
    /// </summary>
    public string FileSystemType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the filesystem is read-only.
    /// </summary>
    public bool IsReadOnly { get; set; }
}
