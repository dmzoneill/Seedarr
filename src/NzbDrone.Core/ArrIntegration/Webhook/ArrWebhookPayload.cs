using System.Collections.Generic;
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

    [JsonPropertyName("artist")]
    public ArrWebhookArtist Artist { get; set; }

    [JsonPropertyName("album")]
    public ArrWebhookAlbum Album { get; set; }

    [JsonPropertyName("tracks")]
    public List<ArrWebhookTrack> Tracks { get; set; }

    [JsonPropertyName("author")]
    public ArrWebhookAuthor Author { get; set; }

    [JsonPropertyName("book")]
    public ArrWebhookBook Book { get; set; }

    [JsonPropertyName("bookFiles")]
    public List<ArrWebhookBookFile> BookFiles { get; set; }
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

public class ArrWebhookArtist
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }
}

public class ArrWebhookAlbum
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; }
}

public class ArrWebhookTrack
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("trackNumber")]
    public string TrackNumber { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }
}

public class ArrWebhookAuthor
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }
}

public class ArrWebhookBook
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; }
}

public class ArrWebhookBookFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("quality")]
    public string Quality { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
