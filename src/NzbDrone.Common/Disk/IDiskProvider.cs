namespace NzbDrone.Common.Disk;

public interface IDiskProvider
{
    /// <summary>
    /// Returns available free bytes on the volume containing <paramref name="path"/>.
    /// <c>null</c> when the query fails (I/O error, drive not ready). <c>0</c> for blank paths.
    /// </summary>
    long? GetAvailableFreeSpace(string path);

    bool CheckFolderWritable(string path);

    bool IsValidFileName(string name);

    string SanitizeFileName(string name);

    bool IsValidPath(string path);

    bool ContainsPathTraversal(string path);
}
