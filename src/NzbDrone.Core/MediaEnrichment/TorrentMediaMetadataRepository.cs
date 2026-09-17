using System;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaEnrichment;

public class TorrentMediaMetadataRepository : BasicRepository<TorrentMediaMetadata>, ITorrentMediaMetadataRepository
{
    private readonly IDatabase _database;
    private readonly string _upsertSql;

    public TorrentMediaMetadataRepository(IDatabase database)
        : base(database)
    {
        _database = database;
        _upsertSql = $@"
INSERT INTO ""{_table}"" (
    ""TorrentId"", ""ArrType"", ""ArrMediaId"", ""Title"", ""Year"", ""Overview"",
    ""PosterUrl"", ""PosterLocalPath"", ""BackdropUrl"", ""BackdropLocalPath"",
    ""MediaInfoJson"", ""Genres"", ""Rating"", ""ImdbId"", ""TmdbId"", ""TvdbId"",
    ""BannerUrl"", ""MusicBrainzId"", ""ArtistName"", ""AlbumTitle"", ""Cast"",
    ""Studio"", ""SiteName"", ""Performers"", ""SceneCode"", ""ReleaseDate"",
    ""Author"", ""BookTitle"", ""Isbn"", ""Publisher"", ""PageCount"",
    ""PackagingFormat"", ""SeriesName"", ""SeriesPosition"", ""Asin""
) VALUES (
    @TorrentId, @ArrType, @ArrMediaId, @Title, @Year, @Overview,
    @PosterUrl, @PosterLocalPath, @BackdropUrl, @BackdropLocalPath,
    @MediaInfoJson, @Genres, @Rating, @ImdbId, @TmdbId, @TvdbId,
    @BannerUrl, @MusicBrainzId, @ArtistName, @AlbumTitle, @Cast,
    @Studio, @SiteName, @Performers, @SceneCode, @ReleaseDate,
    @Author, @BookTitle, @Isbn, @Publisher, @PageCount,
    @PackagingFormat, @SeriesName, @SeriesPosition, @Asin
)
ON CONFLICT(""TorrentId"") DO UPDATE SET
    ""ArrType"" = excluded.""ArrType"",
    ""ArrMediaId"" = excluded.""ArrMediaId"",
    ""Title"" = excluded.""Title"",
    ""Year"" = excluded.""Year"",
    ""Overview"" = excluded.""Overview"",
    ""PosterUrl"" = excluded.""PosterUrl"",
    ""PosterLocalPath"" = excluded.""PosterLocalPath"",
    ""BackdropUrl"" = excluded.""BackdropUrl"",
    ""BackdropLocalPath"" = excluded.""BackdropLocalPath"",
    ""MediaInfoJson"" = excluded.""MediaInfoJson"",
    ""Genres"" = excluded.""Genres"",
    ""Rating"" = excluded.""Rating"",
    ""ImdbId"" = excluded.""ImdbId"",
    ""TmdbId"" = excluded.""TmdbId"",
    ""TvdbId"" = excluded.""TvdbId"",
    ""BannerUrl"" = excluded.""BannerUrl"",
    ""MusicBrainzId"" = excluded.""MusicBrainzId"",
    ""ArtistName"" = excluded.""ArtistName"",
    ""AlbumTitle"" = excluded.""AlbumTitle"",
    ""Cast"" = excluded.""Cast"",
    ""Studio"" = excluded.""Studio"",
    ""SiteName"" = excluded.""SiteName"",
    ""Performers"" = excluded.""Performers"",
    ""SceneCode"" = excluded.""SceneCode"",
    ""ReleaseDate"" = excluded.""ReleaseDate"",
    ""Author"" = excluded.""Author"",
    ""BookTitle"" = excluded.""BookTitle"",
    ""Isbn"" = excluded.""Isbn"",
    ""Publisher"" = excluded.""Publisher"",
    ""PageCount"" = excluded.""PageCount"",
    ""PackagingFormat"" = excluded.""PackagingFormat"",
    ""SeriesName"" = excluded.""SeriesName"",
    ""SeriesPosition"" = excluded.""SeriesPosition"",
    ""Asin"" = excluded.""Asin""
RETURNING ""Id"";";
    }

    public TorrentMediaMetadata GetByTorrentId(int torrentId)
    {
        return RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            return connection.QueryFirstOrDefault<TorrentMediaMetadata>(
                $"SELECT * FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId });
        });
    }

    public void DeleteByTorrentId(int torrentId)
    {
        RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            connection.Execute(
                $"DELETE FROM \"{_table}\" WHERE \"TorrentId\" = @TorrentId",
                new { TorrentId = torrentId });
        });
    }

    public TorrentMediaMetadata Upsert(TorrentMediaMetadata metadata)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (string.IsNullOrEmpty(metadata.ArrType))
        {
            metadata.ArrType = "Unknown";
        }

        if (string.IsNullOrEmpty(metadata.Title))
        {
            metadata.Title = string.Empty;
        }

        return RetryPolicy.Execute(() =>
        {
            using var connection = _database.OpenConnection();
            var id = connection.ExecuteScalar<int>(_upsertSql, metadata);
            if (id > 0)
            {
                metadata.Id = id;
            }

            return metadata;
        });
    }
}
