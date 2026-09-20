namespace NzbDrone.Core.Configuration;

/// <summary>
/// Specifies the disk space preallocation mode for torrent downloads.
/// </summary>
public enum PreallocationMode
{
    None = 0,
    Sparse = 1,
    Full = 2,
}
