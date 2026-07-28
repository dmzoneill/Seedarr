using System.Text.Json.Serialization;

namespace NzbDrone.Core.ArrIntegration.Webhook;

public class ArrWebhookPayload
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; }

    [JsonPropertyName("instanceName")]
    public string InstanceName { get; set; }

    [JsonPropertyName("applicationUrl")]
    public string ApplicationUrl { get; set; }

    [JsonPropertyName("downloadClient")]
    public string DownloadClient { get; set; }

    [JsonPropertyName("downloadClientType")]
    public string DownloadClientType { get; set; }

    [JsonPropertyName("downloadId")]
    public string DownloadId { get; set; }

    [JsonPropertyName("release")]
    public ArrWebhookRelease Release { get; set; }
}

public class ArrWebhookRelease
{
    [JsonPropertyName("releaseTitle")]
    public string ReleaseTitle { get; set; }

    [JsonPropertyName("indexer")]
    public string Indexer { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("quality")]
    public string Quality { get; set; }

    [JsonPropertyName("releaseGroup")]
    public string ReleaseGroup { get; set; }

    [JsonPropertyName("indexerFlags")]
    public string[] IndexerFlags { get; set; }
}
