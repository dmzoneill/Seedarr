using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MediaEnrichment;

namespace Seedarr.Api.V1.MediaCover;

public static class MediaMetadataResourceMapper
{
    public static MediaMetadataResource ToResource(TorrentMediaMetadata model)
    {
        if (model == null)
        {
            return null;
        }

        var posterUrl = model.TorrentId > 0 && !string.IsNullOrEmpty(model.PosterLocalPath)
            ? $"/api/v1/mediacover/{model.TorrentId}/poster.jpg"
            : model.PosterUrl;

        var backdropUrl = model.TorrentId > 0 && !string.IsNullOrEmpty(model.BackdropLocalPath)
            ? $"/api/v1/mediacover/{model.TorrentId}/backdrop.jpg"
            : model.BackdropUrl;

        return new MediaMetadataResource
        {
            Id = model.Id,
            TorrentId = model.TorrentId,
            ArrType = model.ArrType,
            ArrMediaId = model.ArrMediaId,
            Title = model.Title,
            Year = model.Year,
            Overview = model.Overview,
            PosterUrl = posterUrl,
            BackdropUrl = backdropUrl,
            BannerUrl = model.BannerUrl,
            MediaInfoJson = model.MediaInfoJson,
            Genres = string.IsNullOrWhiteSpace(model.Genres)
                ? new List<string>()
                : model.Genres.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0).ToList(),
            Rating = model.Rating,
            ImdbId = model.ImdbId,
            TmdbId = model.TmdbId,
            TvdbId = model.TvdbId,
            MusicBrainzId = model.MusicBrainzId,
            ArtistName = model.ArtistName,
            AlbumTitle = model.AlbumTitle,
            Cast = string.IsNullOrWhiteSpace(model.Cast)
                ? new List<string>()
                : model.Cast.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList(),
            Studio = model.Studio,
            SiteName = model.SiteName,
            Performers = string.IsNullOrWhiteSpace(model.Performers)
                ? new List<string>()
                : model.Performers.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList(),
            SceneCode = model.SceneCode,
            ReleaseDate = model.ReleaseDate,
            Edition = model.Edition,
            Author = model.Author,
            BookTitle = model.BookTitle,
            Isbn = model.Isbn,
            Publisher = model.Publisher,
            PageCount = model.PageCount,
            PackagingFormat = model.PackagingFormat,
            SeriesName = model.SeriesName,
            SeriesPosition = model.SeriesPosition,
            Asin = model.Asin,
        };
    }
}
