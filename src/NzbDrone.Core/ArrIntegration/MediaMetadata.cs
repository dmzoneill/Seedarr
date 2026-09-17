using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ArrIntegration
{
    public class MediaMetadata
    {
        public string MediaType { get; set; } // "movie", "series", "album"
        public int? MediaId { get; set; }
        public string Title { get; set; }
        public int? Year { get; set; }
        public string Overview { get; set; }
        public string PosterUrl { get; set; }
        public string FanartUrl { get; set; }
        public string BannerUrl { get; set; }
        public List<string> Genres { get; set; } = new List<string>();
        public List<MediaActor> Actors { get; set; } = new List<MediaActor>();
        public string StudioOrNetwork { get; set; }
        public double? Rating { get; set; }
        public string ImdbId { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string MusicBrainzId { get; set; }
        public string Studio { get; set; }
        public string SiteName { get; set; }
        public string Performers { get; set; }
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

    public class MediaActor
    {
        public string Name { get; set; }
        public string Character { get; set; }
        public string ImageUrl { get; set; }
    }
}
