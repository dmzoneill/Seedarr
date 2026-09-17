namespace Seedarr.Api.V1.FileSystem;

public class DirectoryValidationRequest
{
    public string Path { get; set; }

    public bool TestWrite { get; set; } = true;
}
