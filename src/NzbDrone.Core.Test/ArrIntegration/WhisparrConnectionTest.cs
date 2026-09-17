using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Test.TestHelpers;
using Polly;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class WhisparrConnectionTest
{
    private WhisparrConnection _connection;

    [SetUp]
    public void Setup()
    {
        _connection = new WhisparrConnection();
    }

    private static WhisparrConnection CreateWithMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var policy = new ResiliencePipelineBuilder().Build();
        return new WhisparrConnection(httpClient, policy);
    }

    [Test]
    public void Name_should_return_whisparr()
    {
        Assert.That(_connection.Name, Is.EqualTo("Whisparr"));
    }

    [Test]
    public void ArrType_should_return_whisparr()
    {
        Assert.That(_connection.ArrType, Is.EqualTo("Whisparr"));
    }

    [Test]
    public void Default_url_should_be_localhost_6969()
    {
        Assert.That(_connection.Url, Is.EqualTo("http://localhost:6969"));
    }

    [Test]
    public void Default_api_key_should_be_empty()
    {
        Assert.That(_connection.ApiKey, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Url_should_be_settable()
    {
        _connection.Url = "http://whisparr.local:6969";
        Assert.That(_connection.Url, Is.EqualTo("http://whisparr.local:6969"));
    }

    [Test]
    public void ApiKey_should_be_settable()
    {
        _connection.ApiKey = "whisparr-api-key";
        Assert.That(_connection.ApiKey, Is.EqualTo("whisparr-api-key"));
    }

    [Test]
    public void TestConnection_should_return_true_when_status_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""version"":""3.0""}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:6969";
        connection.ApiKey = "test-key";

        var result = connection.TestConnection();

        Assert.That(result, Is.True);
    }

    [Test]
    public void TestConnection_should_return_false_when_unauthorized()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, @"{}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:6969";
        connection.ApiKey = "bad-key";

        var result = connection.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void TestConnection_should_return_false_when_internal_server_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, @"{}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:6969";
        connection.ApiKey = "test-key";

        var result = connection.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetDownloadHistory_parses_grabbed_records()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Scene Title 2024"",""downloadId"":""dl-123"",""date"":""2024-01-15T10:30:00Z"",""movieId"":42,""data"":{""torrentInfoHash"":""abc123def"",""indexer"":""AdultIndexer"",""downloadClient"":""qBittorrent"",""downloadUrl"":""https://example.com/torrent""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:6969";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Scene Title 2024"));
        Assert.That(result[0].DownloadId, Is.EqualTo("dl-123"));
        Assert.That(result[0].InfoHash, Is.EqualTo("abc123def"));
        Assert.That(result[0].Indexer, Is.EqualTo("AdultIndexer"));
        Assert.That(result[0].DownloadClient, Is.EqualTo("qBittorrent"));
        Assert.That(result[0].DownloadUrl, Is.EqualTo("https://example.com/torrent"));
        Assert.That(result[0].MediaId, Is.EqualTo(42));
    }

    [Test]
    public void GetDownloadHistory_skips_non_grabbed_events()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""downloadFolderImported"",""sourceTitle"":""Imported"",""downloadId"":""dl-1"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Grabbed"",""downloadId"":""dl-2"",""data"":{""torrentInfoHash"":""hash2""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:6969";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Grabbed"));
        Assert.That(result[0].InfoHash, Is.EqualTo("hash2"));
    }

    [Test]
    public void GetDownloadHistory_skips_records_without_info_hash()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""grabbed"",""sourceTitle"":""No Hash"",""downloadId"":""dl-1"",""data"":{}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Has Hash"",""downloadId"":""dl-2"",""data"":{""torrentInfoHash"":""validhash""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:6969";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("validhash"));
    }

    [Test]
    public void GetMediaDetails_parses_adult_metadata()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            @"{" +
            @"""id"":42," +
            @"""title"":""Big Studio Scene""," +
            @"""year"":2024," +
            @"""overview"":""A high-production scene.""," +
            @"""studio"":""Naughty America""," +
            @"""siteName"":""MySite""," +
            @"""performers"":[""Jane Doe"",""John Smith""]," +
            @"""sceneCode"":""NA-1234""," +
            @"""releaseDate"":""2024-03-15T00:00:00Z""," +
            @"""tmdbId"":1111," +
            @"""imdbId"":""tt0000001""," +
            @"""genres"":[""Adult"",""Hardcore""]," +
            @"""ratings"":{""imdb"":{""value"":8.5}}," +
            @"""images"":[" +
            @"{""coverType"":""poster"",""url"":""/MediaCover/42/poster.jpg""}," +
            @"{""coverType"":""fanart"",""url"":""/MediaCover/42/fanart.jpg""}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:6969";
        connection.ApiKey = "test-key";
        connection.ConnectionId = 5;

        var result = connection.GetMediaDetails(42);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Title, Is.EqualTo("Big Studio Scene"));
        Assert.That(result.Year, Is.EqualTo(2024));
        Assert.That(result.Overview, Is.EqualTo("A high-production scene."));
        Assert.That(result.Studio, Is.EqualTo("Naughty America"));
        Assert.That(result.StudioOrNetwork, Is.EqualTo("Naughty America"));
        Assert.That(result.SiteName, Is.EqualTo("MySite"));
        Assert.That(result.Performers, Is.EqualTo("Jane Doe, John Smith"));
        Assert.That(result.SceneCode, Is.EqualTo("NA-1234"));
        Assert.That(result.ReleaseDate, Is.EqualTo(new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(result.TmdbId, Is.EqualTo(1111));
        Assert.That(result.ImdbId, Is.EqualTo("tt0000001"));
        Assert.That(result.Genres, Contains.Item("Adult"));
        Assert.That(result.Rating, Is.EqualTo(8.5));
        Assert.That(result.PosterUrl, Is.EqualTo("/api/v1/arr/image-proxy?connectionId=5&path=%2FMediaCover%2F42%2Fposter.jpg"));
        Assert.That(result.FanartUrl, Is.EqualTo("/api/v1/arr/image-proxy?connectionId=5&path=%2FMediaCover%2F42%2Ffanart.jpg"));
    }

    [Test]
    public void LookupMedia_parses_adult_metadata_from_search()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            @"[{" +
            @"""id"":100," +
            @"""title"":""Brazzers Scene""," +
            @"""year"":2023," +
            @"""studio"":{""name"":""Brazzers""}," +
            @"""site"":{""name"":""Brazzers Exxtra""}," +
            @"""foreignId"":""BZ-999""," +
            @"""performers"":""Alice Wonder""," +
            @"""releaseDate"":""2023-11-20T00:00:00Z""" +
            @"}]");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:6969";
        connection.ApiKey = "test-key";

        var result = connection.LookupMedia("Brazzers Scene");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaId, Is.EqualTo(100));
        Assert.That(result.Title, Is.EqualTo("Brazzers Scene"));
        Assert.That(result.Year, Is.EqualTo(2023));
        Assert.That(result.Studio, Is.EqualTo("Brazzers"));
        Assert.That(result.SiteName, Is.EqualTo("Brazzers Exxtra"));
        Assert.That(result.SceneCode, Is.EqualTo("BZ-999"));
        Assert.That(result.Performers, Is.EqualTo("Alice Wonder"));
        Assert.That(result.ReleaseDate, Is.EqualTo(new DateTime(2023, 11, 20, 0, 0, 0, DateTimeKind.Utc)));
    }
}
