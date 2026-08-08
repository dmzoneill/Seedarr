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
    }

    public class MediaActor
    {
        public string Name { get; set; }
        public string Character { get; set; }
        public string ImageUrl { get; set; }
    }
}
