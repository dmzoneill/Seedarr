namespace NzbDrone.Common.Disk;

public interface IDiskProvider
{
    long GetAvailableFreeSpace(string path);

    bool CheckFolderWritable(string path);

    bool IsValidFileName(string name);

    string SanitizeFileName(string name);

    bool IsValidPath(string path);

    bool ContainsPathTraversal(string path);
}
