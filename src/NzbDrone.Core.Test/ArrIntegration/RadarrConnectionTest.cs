using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.Test.TestHelpers;
using Polly;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class RadarrConnectionTest
{
    private RadarrConnection _connection;
    private static HttpClient _originalClient;
    private static ResiliencePipeline _originalPolicy;
    private static bool _canReplaceStaticFields;

    [OneTimeSetUp]
    public void FixtureSetup()
    {
        _canReplaceStaticFields = TryGetStaticFields(out _originalClient, out _originalPolicy);
    }

    [OneTimeTearDown]
    public void FixtureTearDown()
    {
        if (_canReplaceStaticFields)
        {
            RestoreStaticFields(_originalClient, _originalPolicy);
        }
    }

    [SetUp]
    public void Setup()
    {
        _connection = new RadarrConnection();
    }

    private static bool TryGetStaticFields(out HttpClient client, out ResiliencePipeline policy)
    {
        try
        {
            var clientField = typeof(RadarrConnection).GetField("Client",
                BindingFlags.NonPublic | BindingFlags.Static);
            var policyField = typeof(RadarrConnection).GetField("Policy",
                BindingFlags.NonPublic | BindingFlags.Static);

            client = (HttpClient)clientField.GetValue(null);
            policy = (ResiliencePipeline)policyField.GetValue(null);
            return true;
        }
        catch
        {
            client = null;
            policy = null;
            return false;
        }
    }

    private static void RestoreStaticFields(HttpClient client, ResiliencePipeline policy)
    {
        try
        {
            var clientField = typeof(RadarrConnection).GetField("Client",
                BindingFlags.NonPublic | BindingFlags.Static);
            var policyField = typeof(RadarrConnection).GetField("Policy",
                BindingFlags.NonPublic | BindingFlags.Static);

            clientField.SetValue(null, client);
            policyField.SetValue(null, policy);
        }
        catch
        {
            // Restoration failed; static fields are unchanged.
        }
    }

    private bool InjectMockClient(MockHttpMessageHandler handler)
    {
        try
        {
            var clientField = typeof(RadarrConnection).GetField("Client",
                BindingFlags.NonPublic | BindingFlags.Static);
            var policyField = typeof(RadarrConnection).GetField("Policy",
                BindingFlags.NonPublic | BindingFlags.Static);

            clientField.SetValue(null, new HttpClient(handler));
            policyField.SetValue(null, new ResiliencePipelineBuilder().Build());
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Test]
    public void Name_should_return_radarr()
    {
        Assert.That(_connection.Name, Is.EqualTo("Radarr"));
    }

    [Test]
    public void ArrType_should_return_radarr()
    {
        Assert.That(_connection.ArrType, Is.EqualTo("Radarr"));
    }

    [Test]
    public void Default_url_should_be_localhost_7878()
    {
        Assert.That(_connection.Url, Is.EqualTo("http://localhost:7878"));
    }

    [Test]
    public void Default_api_key_should_be_empty()
    {
        Assert.That(_connection.ApiKey, Is.EqualTo(""));
    }

    [Test]
    public void Url_should_be_settable()
    {
        _connection.Url = "http://radarr.local:7878";

        Assert.That(_connection.Url, Is.EqualTo("http://radarr.local:7878"));
    }

    [Test]
    public void ApiKey_should_be_settable()
    {
        _connection.ApiKey = "radarr-api-key";

        Assert.That(_connection.ApiKey, Is.EqualTo("radarr-api-key"));
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
    public void GetDownloadHistory_should_parse_grabbed_records()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Movie Title 2024"",""downloadId"":""dl-123"",""date"":""2024-01-15T10:30:00Z"",""data"":{""torrentInfoHash"":""abc123def"",""indexer"":""MyIndexer"",""downloadClient"":""qBittorrent"",""downloadUrl"":""https://example.com/t""}}]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Movie Title 2024"));
        Assert.That(result[0].DownloadId, Is.EqualTo("dl-123"));
        Assert.That(result[0].InfoHash, Is.EqualTo("abc123def"));
        Assert.That(result[0].Indexer, Is.EqualTo("MyIndexer"));
        Assert.That(result[0].DownloadClient, Is.EqualTo("qBittorrent"));
        Assert.That(result[0].DownloadUrl, Is.EqualTo("https://example.com/t"));
    }

    [Test]
    public void GetDownloadHistory_should_skip_non_grabbed_events()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""downloadImported"",""sourceTitle"":""Imported"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Grabbed"",""downloadId"":""dl-2"",""date"":""2024-01-15T10:30:00Z"",""data"":{""torrentInfoHash"":""hash2""}}," +
            @"{""eventType"":""deleted"",""sourceTitle"":""Deleted"",""downloadId"":""dl-3"",""date"":""2024-01-15T11:00:00Z"",""data"":{""torrentInfoHash"":""hash3""}}" +
            @"]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Grabbed"));
        Assert.That(result[0].InfoHash, Is.EqualTo("hash2"));
    }

    [Test]
    public void GetDownloadHistory_should_skip_records_without_info_hash()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""grabbed"",""sourceTitle"":""No Hash"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z"",""data"":{}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Has Hash"",""downloadId"":""dl-2"",""date"":""2024-01-15T10:30:00Z"",""data"":{""torrentInfoHash"":""validhash""}}" +
            @"]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("validhash"));
    }

    [Test]
    public void GetDownloadHistory_should_handle_record_without_data_section()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""No Data"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z""}]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        // Record has no data section, so InfoHash is null -> skipped
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_should_return_empty_when_non_success_status()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, @"{""error"":""unauthorized""}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "bad-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_should_return_empty_when_no_records_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""page"":1,""totalRecords"":0}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_should_handle_missing_optional_fields()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""data"":{""torrentInfoHash"":""abc""}}]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo(""));
        Assert.That(result[0].DownloadId, Is.EqualTo(""));
        Assert.That(result[0].InfoHash, Is.EqualTo("abc"));
    }

    [Test]
    public void GetDownloadHistory_should_handle_multiple_grabbed_records()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Movie 1"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Movie 2"",""downloadId"":""dl-2"",""date"":""2024-01-15T11:00:00Z"",""data"":{""torrentInfoHash"":""hash2""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Movie 3"",""downloadId"":""dl-3"",""date"":""2024-01-15T12:00:00Z"",""data"":{""torrentInfoHash"":""hash3""}}" +
            @"]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void TestConnection_should_return_true_when_status_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""version"":""5.0""}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.TestConnection();

        Assert.That(result, Is.True);
    }

    [Test]
    public void TestConnection_should_return_false_when_unauthorized()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, @"{}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "bad-key";

        var result = _connection.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetDownloadHistory_should_return_empty_when_records_array_is_empty()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""records"":[]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_should_skip_grabbed_record_with_empty_string_info_hash()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Movie"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z"",""data"":{""torrentInfoHash"":""""}}]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        // Empty string info hash is treated as "no hash" and skipped
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_should_populate_indexer_download_client_and_url_fields()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Film"",""downloadId"":""dl-x"",""date"":""2024-06-01T12:00:00Z"",""data"":{""torrentInfoHash"":""hashval"",""indexer"":""NzbGeek"",""downloadClient"":""Deluge"",""downloadUrl"":""https://tracker.example.com/dl""}}]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Indexer, Is.EqualTo("NzbGeek"));
        Assert.That(result[0].DownloadClient, Is.EqualTo("Deluge"));
        Assert.That(result[0].DownloadUrl, Is.EqualTo("https://tracker.example.com/dl"));
    }

    [Test]
    public void GetDownloadHistory_should_use_utcnow_when_date_property_is_missing()
    {
        var before = DateTime.UtcNow;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""data"":{""torrentInfoHash"":""datelesshash""}}]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();
        var after = DateTime.UtcNow;

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Date, Is.GreaterThanOrEqualTo(before));
        Assert.That(result[0].Date, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void GetDownloadHistory_should_return_empty_when_internal_server_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, @"{}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    // --- Constructor-injection tests (bypass static readonly field limitation) ---

    private static RadarrConnection CreateWithMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var policy = new ResiliencePipelineBuilder().Build();
        return new RadarrConnection(httpClient, policy);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_parses_all_fields()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Movie Title 2024"",""downloadId"":""dl-123"",""date"":""2024-01-15T10:30:00Z"",""data"":{""torrentInfoHash"":""abc123def"",""indexer"":""MyIndexer"",""downloadClient"":""qBittorrent"",""downloadUrl"":""https://example.com/t""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Movie Title 2024"));
        Assert.That(result[0].DownloadId, Is.EqualTo("dl-123"));
        Assert.That(result[0].InfoHash, Is.EqualTo("abc123def"));
        Assert.That(result[0].Indexer, Is.EqualTo("MyIndexer"));
        Assert.That(result[0].DownloadClient, Is.EqualTo("qBittorrent"));
        Assert.That(result[0].DownloadUrl, Is.EqualTo("https://example.com/t"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_skips_non_grabbed_events()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""downloadImported"",""sourceTitle"":""Imported"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Grabbed"",""downloadId"":""dl-2"",""date"":""2024-01-15T10:30:00Z"",""data"":{""torrentInfoHash"":""hash2""}}," +
            @"{""eventType"":""deleted"",""sourceTitle"":""Deleted"",""downloadId"":""dl-3"",""date"":""2024-01-15T11:00:00Z"",""data"":{""torrentInfoHash"":""hash3""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Grabbed"));
        Assert.That(result[0].InfoHash, Is.EqualTo("hash2"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_skips_records_without_info_hash()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""grabbed"",""sourceTitle"":""No Hash"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z"",""data"":{}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Has Hash"",""downloadId"":""dl-2"",""date"":""2024-01-15T10:30:00Z"",""data"":{""torrentInfoHash"":""validhash""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
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
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""No Data"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z""}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
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
        connection.Url = "http://localhost:7878";
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
        connection.Url = "http://localhost:7878";
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
        connection.Url = "http://localhost:7878";
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
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Movie"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z"",""data"":{""torrentInfoHash"":""""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_handles_missing_optional_fields()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""data"":{""torrentInfoHash"":""abc""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo(""));
        Assert.That(result[0].DownloadId, Is.EqualTo(""));
        Assert.That(result[0].InfoHash, Is.EqualTo("abc"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_uses_utcnow_when_date_property_missing()
    {
        var before = DateTime.UtcNow;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""data"":{""torrentInfoHash"":""datelesshash""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
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
            @"{""eventType"":""grabbed"",""sourceTitle"":""Movie 1"",""downloadId"":""dl-1"",""date"":""2024-01-15T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Movie 2"",""downloadId"":""dl-2"",""date"":""2024-01-15T11:00:00Z"",""data"":{""torrentInfoHash"":""hash2""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Movie 3"",""downloadId"":""dl-3"",""date"":""2024-01-15T12:00:00Z"",""data"":{""torrentInfoHash"":""hash3""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Title, Is.EqualTo("Movie 1"));
        Assert.That(result[2].Title, Is.EqualTo("Movie 3"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_parses_date_correctly()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Test"",""downloadId"":""dl-1"",""date"":""2024-06-15T08:30:00Z"",""data"":{""torrentInfoHash"":""testhash123""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Date, Is.EqualTo(new DateTime(2024, 6, 15, 8, 30, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_returns_empty_when_internal_server_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, @"{}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TestConnection_with_injected_client_returns_true_when_status_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""version"":""5.0""}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:7878";
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
        connection.Url = "http://localhost:7878";
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
        connection.Url = "http://localhost:7878";
        connection.ApiKey = "test-key";

        var result = connection.TestConnection();

        Assert.That(result, Is.False);
    }
}
