using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Tags;

public class TagResource : RestResource
{
    public string Label { get; set; }
    public string Color { get; set; }
    public int? UploadLimitKbps { get; set; }
    public int? DownloadLimitKbps { get; set; }
    public double? MinSeedRatio { get; set; }
    public int? MinSeedTimeSeconds { get; set; }
    public int TorrentCount { get; set; }
}
