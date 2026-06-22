namespace NzbDrone.Core.DiskSpace;

/// <summary>
/// Represents disk space information for a single drive or mount point.
/// </summary>
public class DiskSpaceInfo
{
    /// <summary>
    /// Gets or sets the filesystem path (mount point or drive root).
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// Gets or sets a human-friendly label for this location.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// Gets or sets the free space in bytes.
    /// </summary>
    public long FreeSpace { get; set; }

    /// <summary>
    /// Gets or sets the total size in bytes.
    /// </summary>
    public long TotalSpace { get; set; }
}
