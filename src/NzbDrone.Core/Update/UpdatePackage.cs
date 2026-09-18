namespace NzbDrone.Core.Update;

public class UpdatePackage
{
    public string FileName { get; set; }
    public string DownloadUrl { get; set; }
    public string Sha256ChecksumUrl { get; set; }
    public long Size { get; set; }
    public string Platform { get; set; }
}
