using System.Text.Json.Serialization;

namespace NzbDrone.Core.Update;

public class ReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; }

    public ReleaseAsset()
    {
    }

    public ReleaseAsset(string name, string downloadUrl, long size = 0, string contentType = null)
    {
        Name = name;
        DownloadUrl = downloadUrl;
        Size = size;
        ContentType = contentType;
    }
}
