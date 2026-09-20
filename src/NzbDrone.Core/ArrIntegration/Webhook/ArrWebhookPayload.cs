using System;
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

    [JsonPropertyName("series")]
    public ArrWebhookSeries Series { get; set; }

    [JsonPropertyName("episodes")]
    public List<ArrWebhookEpisode> Episodes { get; set; }

    [JsonPropertyName("episodeFile")]
    public ArrWebhookEpisodeFile EpisodeFile { get; set; }

    [JsonPropertyName("movie")]
    public ArrWebhookMovie Movie { get; set; }

    [JsonPropertyName("movieFile")]
    public ArrWebhookMovieFile MovieFile { get; set; }

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

    [JsonPropertyName("deletedFiles")]
    public List<string> DeletedFiles { get; set; }

    [JsonPropertyName("deleteFiles")]
    public bool? DeleteFiles { get; set; }

    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; }

    [JsonPropertyName("destinationPath")]
    public string DestinationPath { get; set; }

    [JsonPropertyName("renamedFiles")]
    public List<ArrWebhookRenamedFile> RenamedFiles { get; set; }

    [JsonPropertyName("renamedEpisodeFiles")]
    public List<ArrWebhookRenamedFile> RenamedEpisodeFiles { get; set; }

    [JsonPropertyName("renamedMovieFiles")]
    public List<ArrWebhookRenamedFile> RenamedMovieFiles { get; set; }
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

public class ArrWebhookSeries
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("tvdbId")]
    public int TvdbId { get; set; }

    [JsonPropertyName("tvMazeId")]
    public int TvMazeId { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }
}

public class ArrWebhookEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("airDate")]
    public string AirDate { get; set; }

    [JsonPropertyName("airDateUtc")]
    public DateTime? AirDateUtc { get; set; }
}

public class ArrWebhookEpisodeFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("quality")]
    public string Quality { get; set; }

    [JsonPropertyName("qualityVersion")]
    public int QualityVersion { get; set; }

    [JsonPropertyName("releaseGroup")]
    public string ReleaseGroup { get; set; }

    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("dateAdded")]
    public DateTime? DateAdded { get; set; }
}

public class ArrWebhookMovie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("folderPath")]
    public string FolderPath { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; }

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; }
}

public class ArrWebhookMovieFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("quality")]
    public string Quality { get; set; }

    [JsonPropertyName("qualityVersion")]
    public int QualityVersion { get; set; }

    [JsonPropertyName("releaseGroup")]
    public string ReleaseGroup { get; set; }

    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("dateAdded")]
    public DateTime? DateAdded { get; set; }
}

public class ArrWebhookRenamedFile
{
    [JsonPropertyName("previousRelativePath")]
    public string PreviousRelativePath { get; set; }

    [JsonPropertyName("previousPath")]
    public string PreviousPath { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }
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
