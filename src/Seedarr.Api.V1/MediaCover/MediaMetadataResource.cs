using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.MediaCover;

public class MediaMetadataResource : RestResource
{
    public int TorrentId { get; set; }

    public string ArrType { get; set; }

    public int ArrMediaId { get; set; }

    public string Title { get; set; }

    public int Year { get; set; }

    public string Overview { get; set; }

    public string PosterUrl { get; set; }

    [JsonIgnore]
    public string PosterLocalPath { get; set; }

    public string BackdropUrl { get; set; }

    [JsonIgnore]
    public string BackdropLocalPath { get; set; }

    public string MediaInfoJson { get; set; }

    public List<string> Genres { get; set; } = new();

    public double Rating { get; set; }

    public string ImdbId { get; set; }

    public string TmdbId { get; set; }

    public string TvdbId { get; set; }

    public string BannerUrl { get; set; }

    public string MusicBrainzId { get; set; }

    public string ArtistName { get; set; }

    public string AlbumTitle { get; set; }

    public List<string> Cast { get; set; } = new();

    public string Studio { get; set; }

    public string SiteName { get; set; }

    public List<string> Performers { get; set; } = new();

    public string SceneCode { get; set; }

    public DateTime? ReleaseDate { get; set; }

    public string Edition { get; set; }

    public string Author { get; set; }

    public string BookTitle { get; set; }

    public string Isbn { get; set; }

    public string Publisher { get; set; }

    public int? PageCount { get; set; }

    public string PackagingFormat { get; set; }

    public string SeriesName { get; set; }

    public string SeriesPosition { get; set; }

    public string Asin { get; set; }
}
