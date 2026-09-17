namespace NzbDrone.Core.FileSystem;

public class FileSystemValidationResult
{
    public bool IsValid { get; set; }

    public string ResolvedPath { get; set; }

    public string ErrorMessage { get; set; }
}
