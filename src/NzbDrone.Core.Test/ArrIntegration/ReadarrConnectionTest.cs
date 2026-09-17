using System;
using System.Net;
using System.Net.Http;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Test.TestHelpers;
using Polly;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class ReadarrConnectionTest
{
    private ReadarrConnection _connection;

    [SetUp]
    public void Setup()
    {
        _connection = new ReadarrConnection();
    }

    private static ReadarrConnection CreateWithMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var policy = new ResiliencePipelineBuilder().Build();
        return new ReadarrConnection(httpClient, policy);
    }

    [Test]
    public void Name_should_return_readarr()
    {
        Assert.That(_connection.Name, Is.EqualTo("Readarr"));
    }

    [Test]
    public void ArrType_should_return_readarr()
    {
        Assert.That(_connection.ArrType, Is.EqualTo("Readarr"));
    }

    [Test]
    public void Default_url_should_be_localhost_8787()
    {
        Assert.That(_connection.Url, Is.EqualTo("http://localhost:8787"));
    }

    [Test]
    public void Default_api_key_should_be_empty()
    {
        Assert.That(_connection.ApiKey, Is.EqualTo(""));
    }

    [Test]
    public void Url_should_be_settable()
    {
        _connection.Url = "http://readarr.local:8787";

        Assert.That(_connection.Url, Is.EqualTo("http://readarr.local:8787"));
    }

    [Test]
    public void ApiKey_should_be_settable()
    {
        _connection.ApiKey = "readarr-api-key";

        Assert.That(_connection.ApiKey, Is.EqualTo("readarr-api-key"));
    }

    [Test]
    public void GetDownloadHistory_should_return_empty_list_when_connection_fails()
    {
        _connection.Url = "http://nonexistent.invalid:1";
        _connection.ApiKey = "bad-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TestConnection_should_return_false_when_connection_fails()
    {
        _connection.Url = "http://nonexistent.invalid:1";
        _connection.ApiKey = "bad-key";

        var result = _connection.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_parses_grabbed_record()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Book Title"",""downloadId"":""dl-789"",""date"":""2024-03-01T12:00:00Z"",""bookId"":10,""data"":{""torrentInfoHash"":""readarrhash"",""indexer"":""ReadarrIdx"",""downloadClient"":""qBittorrent"",""downloadUrl"":""https://tracker.example.com/b""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Book Title"));
        Assert.That(result[0].DownloadId, Is.EqualTo("dl-789"));
        Assert.That(result[0].MediaId, Is.EqualTo(10));
        Assert.That(result[0].MediaType, Is.EqualTo("book"));
        Assert.That(result[0].InfoHash, Is.EqualTo("readarrhash"));
        Assert.That(result[0].Indexer, Is.EqualTo("ReadarrIdx"));
        Assert.That(result[0].DownloadClient, Is.EqualTo("qBittorrent"));
        Assert.That(result[0].DownloadUrl, Is.EqualTo("https://tracker.example.com/b"));
    }

    [Test]
    public void GetDownloadHistory_with_author_id_sets_author_media_type()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Author Collection"",""downloadId"":""dl-790"",""date"":""2024-03-01T12:00:00Z"",""authorId"":25,""data"":{""torrentInfoHash"":""authorhash""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].MediaId, Is.EqualTo(25));
        Assert.That(result[0].MediaType, Is.EqualTo("author"));
        Assert.That(result[0].InfoHash, Is.EqualTo("authorhash"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_skips_non_grabbed_events()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""bookFileImported"",""sourceTitle"":""Imported"",""downloadId"":""dl-1"",""date"":""2024-03-01T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Grabbed Book"",""downloadId"":""dl-2"",""date"":""2024-03-01T10:30:00Z"",""data"":{""torrentInfoHash"":""hash2""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("hash2"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_skips_records_without_info_hash()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""grabbed"",""sourceTitle"":""No Hash"",""downloadId"":""dl-1"",""date"":""2024-03-01T10:00:00Z"",""data"":{}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Has Hash"",""downloadId"":""dl-2"",""date"":""2024-03-01T10:30:00Z"",""data"":{""torrentInfoHash"":""validhash""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("validhash"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_handles_record_without_data_section()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""No Data"",""downloadId"":""dl-1"",""date"":""2024-03-01T10:00:00Z""}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_returns_empty_when_api_returns_non_success()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, @"{""error"":""unauthorized""}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "bad-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_returns_empty_when_no_records_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""page"":1,""totalRecords"":0}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_returns_empty_when_records_array_is_empty()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""records"":[]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_skips_empty_string_info_hash()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Book"",""downloadId"":""dl-1"",""date"":""2024-03-01T10:00:00Z"",""data"":{""torrentInfoHash"":""""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_handles_missing_optional_fields()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""data"":{""torrentInfoHash"":""readarrmin""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo(""));
        Assert.That(result[0].DownloadId, Is.EqualTo(""));
        Assert.That(result[0].InfoHash, Is.EqualTo("readarrmin"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_uses_utcnow_when_date_property_missing()
    {
        var before = DateTime.UtcNow;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""data"":{""torrentInfoHash"":""nodatehash""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();
        var after = DateTime.UtcNow;

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Date, Is.GreaterThanOrEqualTo(before));
        Assert.That(result[0].Date, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_handles_multiple_grabbed_records()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Book 1"",""downloadId"":""dl-1"",""date"":""2024-03-01T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Book 2"",""downloadId"":""dl-2"",""date"":""2024-03-01T11:00:00Z"",""data"":{""torrentInfoHash"":""hash2""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Title, Is.EqualTo("Book 1"));
        Assert.That(result[1].Title, Is.EqualTo("Book 2"));
    }

    [Test]
    public void TestConnection_with_injected_client_returns_true_when_status_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""version"":""0.3.0""}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.TestConnection();

        Assert.That(result, Is.True);
    }

    [Test]
    public void TestConnection_with_injected_client_returns_false_when_unauthorized()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, @"{}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "bad-key";

        var result = connection.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void TestConnection_with_injected_client_returns_false_when_internal_server_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, @"{}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";

        var result = connection.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetMediaDetails_extracts_book_fields_and_rewrites_mediacover_url()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            @"{""id"":42,""title"":""Dune"",""overview"":""Desert planet"",""releaseDate"":""1965-08-01T00:00:00Z"",""author"":{""authorName"":""Frank Herbert""},""isbn"":""9780441172719"",""publisher"":""Chilton Books"",""pageCount"":412,""packagingFormat"":""Hardcover"",""asin"":""B000001"",""seriesTitle"":""Dune Chronicles"",""seriesPosition"":""1"",""ratings"":{""value"":4.7},""genres"":[""Science Fiction""],""images"":[{""coverType"":""cover"",""url"":""/MediaCover/42/cover.jpg""}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";
        connection.ConnectionId = 8;

        var result = connection.GetMediaDetails(42);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo("book"));
        Assert.That(result.Title, Is.EqualTo("Dune"));
        Assert.That(result.BookTitle, Is.EqualTo("Dune"));
        Assert.That(result.Overview, Is.EqualTo("Desert planet"));
        Assert.That(result.Year, Is.EqualTo(1965));
        Assert.That(result.Author, Is.EqualTo("Frank Herbert"));
        Assert.That(result.Isbn, Is.EqualTo("9780441172719"));
        Assert.That(result.Publisher, Is.EqualTo("Chilton Books"));
        Assert.That(result.PageCount, Is.EqualTo(412));
        Assert.That(result.PackagingFormat, Is.EqualTo("Hardcover"));
        Assert.That(result.Asin, Is.EqualTo("B000001"));
        Assert.That(result.SeriesName, Is.EqualTo("Dune Chronicles"));
        Assert.That(result.SeriesPosition, Is.EqualTo("1"));
        Assert.That(result.Rating, Is.EqualTo(4.7));
        Assert.That(result.Genres, Contains.Item("Science Fiction"));
        Assert.That(result.PosterUrl, Is.EqualTo("/api/v1/arr/image-proxy?connectionId=8&path=%2FMediaCover%2F42%2Fcover.jpg"));
    }

    [Test]
    public void LookupMedia_extracts_book_fields_and_rewrites_mediacover_url()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            @"[{""book"":{""id"":42,""title"":""Dune"",""overview"":""Desert planet"",""releaseDate"":""1965-08-01T00:00:00Z"",""author"":{""name"":""Frank Herbert""},""isbn"":""9780441172719"",""publisher"":""Chilton Books"",""pageCount"":412,""images"":[{""coverType"":""cover"",""url"":""/MediaCover/42/cover.jpg""}]}}]");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8787";
        connection.ApiKey = "test-key";
        connection.ConnectionId = 8;

        var result = connection.LookupMedia("Dune");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.MediaType, Is.EqualTo("book"));
        Assert.That(result.Title, Is.EqualTo("Dune"));
        Assert.That(result.BookTitle, Is.EqualTo("Dune"));
        Assert.That(result.Author, Is.EqualTo("Frank Herbert"));
        Assert.That(result.Isbn, Is.EqualTo("9780441172719"));
        Assert.That(result.PosterUrl, Is.EqualTo("/api/v1/arr/image-proxy?connectionId=8&path=%2FMediaCover%2F42%2Fcover.jpg"));
    }
}
