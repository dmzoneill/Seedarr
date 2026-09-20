using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

/// <summary>
/// Provides disk space preallocation services for torrent files.
/// </summary>
public interface IDiskAllocationService
{
    /// <summary>
    /// Preallocates disk space for torrent files according to the configured preallocation mode.
    /// </summary>
    /// <param name="torrent">The torrent to preallocate files for.</param>
    /// <param name="files">The list of files to preallocate, or null to use torrent.Files.</param>
    /// <param name="baseDirectory">Optional base directory override for the preallocated files.</param>
    void PreallocateFiles(Torrent torrent, IList<TorrentFile> files, string baseDirectory = null);
}
