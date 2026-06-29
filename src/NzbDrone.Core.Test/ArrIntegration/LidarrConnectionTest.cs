using System;
using System.Net;
using System.Net.Http;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Test.TestHelpers;
using Polly;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class LidarrConnectionTest
{
    private LidarrConnection _connection;

    [SetUp]
    public void Setup()
    {
        _connection = new LidarrConnection();
    }

    private static LidarrConnection CreateWithMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var policy = new ResiliencePipelineBuilder().Build();
        return new LidarrConnection(httpClient, policy);
    }

    [Test]
    public void Name_should_return_lidarr()
    {
        Assert.That(_connection.Name, Is.EqualTo("Lidarr"));
    }

    [Test]
    public void ArrType_should_return_lidarr()
    {
        Assert.That(_connection.ArrType, Is.EqualTo("Lidarr"));
    }

    [Test]
    public void Default_url_should_be_localhost_8686()
    {
        Assert.That(_connection.Url, Is.EqualTo("http://localhost:8686"));
    }

    [Test]
    public void Default_api_key_should_be_empty()
    {
        Assert.That(_connection.ApiKey, Is.EqualTo(""));
    }

    [Test]
    public void Url_should_be_settable()
    {
        _connection.Url = "http://lidarr.local:8686";

        Assert.That(_connection.Url, Is.EqualTo("http://lidarr.local:8686"));
    }

    [Test]
    public void ApiKey_should_be_settable()
    {
        _connection.ApiKey = "lidarr-api-key";

        Assert.That(_connection.ApiKey, Is.EqualTo("lidarr-api-key"));
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

    // --- Constructor-injection tests (uses v1 API path for Lidarr) ---

    [Test]
    public void GetDownloadHistory_with_injected_client_parses_grabbed_record()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Album Title"",""downloadId"":""dl-456"",""date"":""2024-03-01T12:00:00Z"",""data"":{""torrentInfoHash"":""lidarrhash"",""indexer"":""LidarrIdx"",""downloadClient"":""Deluge"",""downloadUrl"":""https://tracker.example.com/l""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8686";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Album Title"));
        Assert.That(result[0].DownloadId, Is.EqualTo("dl-456"));
        Assert.That(result[0].InfoHash, Is.EqualTo("lidarrhash"));
        Assert.That(result[0].Indexer, Is.EqualTo("LidarrIdx"));
        Assert.That(result[0].DownloadClient, Is.EqualTo("Deluge"));
        Assert.That(result[0].DownloadUrl, Is.EqualTo("https://tracker.example.com/l"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_skips_non_grabbed_events()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""albumDownloadImported"",""sourceTitle"":""Imported"",""downloadId"":""dl-1"",""date"":""2024-03-01T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Grabbed Album"",""downloadId"":""dl-2"",""date"":""2024-03-01T10:30:00Z"",""data"":{""torrentInfoHash"":""hash2""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8686";
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
        connection.Url = "http://localhost:8686";
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
        connection.Url = "http://localhost:8686";
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
        connection.Url = "http://localhost:8686";
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
        connection.Url = "http://localhost:8686";
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
        connection.Url = "http://localhost:8686";
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
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Album"",""downloadId"":""dl-1"",""date"":""2024-03-01T10:00:00Z"",""data"":{""torrentInfoHash"":""""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8686";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_handles_missing_optional_fields()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""data"":{""torrentInfoHash"":""lidarrmin""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8686";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo(""));
        Assert.That(result[0].DownloadId, Is.EqualTo(""));
        Assert.That(result[0].InfoHash, Is.EqualTo("lidarrmin"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_uses_utcnow_when_date_property_missing()
    {
        var before = DateTime.UtcNow;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""data"":{""torrentInfoHash"":""nodatehash""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8686";
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
            @"{""eventType"":""grabbed"",""sourceTitle"":""Album 1"",""downloadId"":""dl-1"",""date"":""2024-03-01T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Album 2"",""downloadId"":""dl-2"",""date"":""2024-03-01T11:00:00Z"",""data"":{""torrentInfoHash"":""hash2""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8686";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Title, Is.EqualTo("Album 1"));
        Assert.That(result[1].Title, Is.EqualTo("Album 2"));
    }

    [Test]
    public void TestConnection_with_injected_client_returns_true_when_status_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""version"":""2.0""}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8686";
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
        connection.Url = "http://localhost:8686";
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
        connection.Url = "http://localhost:8686";
        connection.ApiKey = "test-key";

        var result = connection.TestConnection();

        Assert.That(result, Is.False);
    }
}
