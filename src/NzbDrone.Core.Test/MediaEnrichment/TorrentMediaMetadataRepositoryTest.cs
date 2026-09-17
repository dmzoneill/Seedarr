using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaEnrichment;

namespace NzbDrone.Core.Test.MediaEnrichment;

[TestFixture]
public class TorrentMediaMetadataRepositoryTest
{
    private string _connectionString;
    private SqliteConnection _keepAliveConnection;
    private IDatabase _database;
    private TorrentMediaMetadataRepository _subject;

    [SetUp]
    public void SetUp()
    {
        TableRegistration.RegisterTables();
        var dbName = $"testdb_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();

        using var cmd = _keepAliveConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE ""TorrentMediaMetadata"" (
                ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""TorrentId"" INTEGER NOT NULL UNIQUE,
                ""ArrType"" TEXT NOT NULL,
                ""ArrMediaId"" INTEGER NOT NULL DEFAULT 0,
                ""Title"" TEXT NOT NULL,
                ""Year"" INTEGER NOT NULL DEFAULT 0,
                ""Overview"" TEXT NULL,
                ""PosterUrl"" TEXT NULL,
                ""PosterLocalPath"" TEXT NULL,
                ""BackdropUrl"" TEXT NULL,
                ""BackdropLocalPath"" TEXT NULL,
                ""MediaInfoJson"" TEXT NULL,
                ""Genres"" TEXT NULL,
                ""Rating"" REAL NOT NULL DEFAULT 0,
                ""ImdbId"" TEXT NULL,
                ""TmdbId"" TEXT NULL,
                ""TvdbId"" TEXT NULL,
                ""BannerUrl"" TEXT NULL,
                ""MusicBrainzId"" TEXT NULL,
                ""ArtistName"" TEXT NULL,
                ""AlbumTitle"" TEXT NULL,
                ""Cast"" TEXT NULL,
                ""Studio"" TEXT NULL,
                ""SiteName"" TEXT NULL,
                ""Performers"" TEXT NULL,
                ""SceneCode"" TEXT NULL,
                ""ReleaseDate"" DATETIME NULL,
                ""Author"" TEXT NULL,
                ""BookTitle"" TEXT NULL,
                ""Isbn"" TEXT NULL,
                ""Publisher"" TEXT NULL,
                ""PageCount"" INTEGER NULL,
                ""PackagingFormat"" TEXT NULL,
                ""SeriesName"" TEXT NULL,
                ""SeriesPosition"" TEXT NULL,
                ""Asin"" TEXT NULL
            )";
        cmd.ExecuteNonQuery();

        _database = new Database(() => new SqliteConnection(_connectionString), DatabaseType.SQLite);
        _subject = new TorrentMediaMetadataRepository(_database);
    }

    [TearDown]
    public void TearDown()
    {
        _keepAliveConnection.Close();
        _keepAliveConnection.Dispose();
    }

    [Test]
    public void Upsert_WhenRecordDoesNotExist_InsertsRecord()
    {
        var metadata = new TorrentMediaMetadata
        {
            TorrentId = 1,
            ArrType = "Radarr",
            ArrMediaId = 123,
            Title = "Inception",
            Year = 2010,
            Overview = "A thief who steals corporate secrets...",
            PosterUrl = "https://example.com/poster.jpg",
            PosterLocalPath = "/media/poster.jpg",
            BackdropUrl = "https://example.com/backdrop.jpg",
            BackdropLocalPath = "/media/backdrop.jpg",
            MediaInfoJson = "{\"codec\":\"x265\"}",
            Genres = "Action, Sci-Fi",
            Rating = 8.8,
            ImdbId = "tt1375666",
            TmdbId = "27205",
            TvdbId = "1000",
            BannerUrl = "https://example.com/banner.jpg",
            MusicBrainzId = "mbid-1",
            ArtistName = "Hans Zimmer",
            AlbumTitle = "Inception OST",
            Cast = "Leonardo DiCaprio, Joseph Gordon-Levitt"
        };

        var result = _subject.Upsert(metadata);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.GreaterThan(0));

        var fetched = _subject.GetByTorrentId(1);
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched.Id, Is.EqualTo(result.Id));
        Assert.That(fetched.TorrentId, Is.EqualTo(1));
        Assert.That(fetched.ArrType, Is.EqualTo("Radarr"));
        Assert.That(fetched.ArrMediaId, Is.EqualTo(123));
        Assert.That(fetched.Title, Is.EqualTo("Inception"));
        Assert.That(fetched.Year, Is.EqualTo(2010));
        Assert.That(fetched.Overview, Is.EqualTo("A thief who steals corporate secrets..."));
        Assert.That(fetched.PosterUrl, Is.EqualTo("https://example.com/poster.jpg"));
        Assert.That(fetched.Genres, Is.EqualTo("Action, Sci-Fi"));
        Assert.That(fetched.Rating, Is.EqualTo(8.8));
        Assert.That(fetched.ImdbId, Is.EqualTo("tt1375666"));
        Assert.That(fetched.TmdbId, Is.EqualTo("27205"));
        Assert.That(fetched.Cast, Is.EqualTo("Leonardo DiCaprio, Joseph Gordon-Levitt"));
    }

    [Test]
    public void Upsert_WhenRecordAlreadyExists_UpdatesRecordWithoutConstraintViolation()
    {
        var initial = new TorrentMediaMetadata
        {
            TorrentId = 42,
            ArrType = "Sonarr",
            ArrMediaId = 456,
            Title = "Original Show Title",
            Year = 2020,
            ImdbId = "tt1111111",
            TmdbId = "222222"
        };

        var inserted = _subject.Upsert(initial);
        var originalId = inserted.Id;
        Assert.That(originalId, Is.GreaterThan(0));

        var updatedMetadata = new TorrentMediaMetadata
        {
            TorrentId = 42,
            ArrType = "Sonarr",
            ArrMediaId = 789,
            Title = "Updated Show Title",
            Year = 2021,
            ImdbId = "tt3333333",
            TmdbId = "444444",
            Genres = "Drama, Mystery",
            Rating = 9.1
        };

        var updated = _subject.Upsert(updatedMetadata);

        Assert.That(updated.Id, Is.EqualTo(originalId));

        var fetched = _subject.GetByTorrentId(42);
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched.Id, Is.EqualTo(originalId));
        Assert.That(fetched.TorrentId, Is.EqualTo(42));
        Assert.That(fetched.ArrMediaId, Is.EqualTo(789));
        Assert.That(fetched.Title, Is.EqualTo("Updated Show Title"));
        Assert.That(fetched.Year, Is.EqualTo(2021));
        Assert.That(fetched.ImdbId, Is.EqualTo("tt3333333"));
        Assert.That(fetched.TmdbId, Is.EqualTo("444444"));
        Assert.That(fetched.Genres, Is.EqualTo("Drama, Mystery"));
        Assert.That(fetched.Rating, Is.EqualTo(9.1));
    }

    [Test]
    public async Task Upsert_ConcurrentUpdatesForSameTorrent_CompleteSuccessfully()
    {
        var tasks = Enumerable.Range(1, 10).Select(i => Task.Run(() =>
        {
            return _subject.Upsert(new TorrentMediaMetadata
            {
                TorrentId = 99,
                ArrType = "Radarr",
                ArrMediaId = i,
                Title = $"Movie Variant {i}",
                Year = 2000 + i,
                ImdbId = $"tt000000{i}",
                TmdbId = $"{1000 + i}"
            });
        }));

        TorrentMediaMetadata[] results = null;
        Assert.DoesNotThrowAsync(async () =>
        {
            results = await Task.WhenAll(tasks);
        });

        Assert.That(results, Is.Not.Null);
        Assert.That(results, Has.Length.EqualTo(10));

        var fetched = _subject.GetByTorrentId(99);
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched.TorrentId, Is.EqualTo(99));
        Assert.That(fetched.Id, Is.GreaterThan(0));
    }

    [Test]
    public void GetByTorrentId_WhenNotFound_ReturnsNull()
    {
        var result = _subject.GetByTorrentId(9999);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void DeleteByTorrentId_RemovesRecord()
    {
        var metadata = new TorrentMediaMetadata
        {
            TorrentId = 77,
            ArrType = "Radarr",
            Title = "To Delete"
        };

        _subject.Upsert(metadata);
        Assert.That(_subject.GetByTorrentId(77), Is.Not.Null);

        _subject.DeleteByTorrentId(77);
        Assert.That(_subject.GetByTorrentId(77), Is.Null);
    }
}
