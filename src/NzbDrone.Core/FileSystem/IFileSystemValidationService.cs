namespace NzbDrone.Core.FileSystem;

public interface IFileSystemValidationService
{
    FileSystemValidationResult ValidateDirectory(string path, bool testWrite = true);
}
