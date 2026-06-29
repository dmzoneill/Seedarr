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
public class SonarrConnectionTest
{
    private SonarrConnection _connection;
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
        _connection = new SonarrConnection();
    }

    private static bool TryGetStaticFields(out HttpClient client, out ResiliencePipeline policy)
    {
        try
        {
            var clientField = typeof(SonarrConnection).GetField("Client",
                BindingFlags.NonPublic | BindingFlags.Static);
            var policyField = typeof(SonarrConnection).GetField("Policy",
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
            var clientField = typeof(SonarrConnection).GetField("Client",
                BindingFlags.NonPublic | BindingFlags.Static);
            var policyField = typeof(SonarrConnection).GetField("Policy",
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
            var clientField = typeof(SonarrConnection).GetField("Client",
                BindingFlags.NonPublic | BindingFlags.Static);
            var policyField = typeof(SonarrConnection).GetField("Policy",
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
    public void Name_should_return_sonarr()
    {
        Assert.That(_connection.Name, Is.EqualTo("Sonarr"));
    }

    [Test]
    public void ArrType_should_return_sonarr()
    {
        Assert.That(_connection.ArrType, Is.EqualTo("Sonarr"));
    }

    [Test]
    public void Default_url_should_be_localhost_8989()
    {
        Assert.That(_connection.Url, Is.EqualTo("http://localhost:8989"));
    }

    [Test]
    public void Default_api_key_should_be_empty()
    {
        Assert.That(_connection.ApiKey, Is.EqualTo(""));
    }

    [Test]
    public void Url_should_be_settable()
    {
        _connection.Url = "http://sonarr.local:8989";

        Assert.That(_connection.Url, Is.EqualTo("http://sonarr.local:8989"));
    }

    [Test]
    public void ApiKey_should_be_settable()
    {
        _connection.ApiKey = "my-secret-key";

        Assert.That(_connection.ApiKey, Is.EqualTo("my-secret-key"));
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
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Show S01E01"",""downloadId"":""dl-123"",""date"":""2024-02-20T14:00:00Z"",""data"":{""torrentInfoHash"":""sonarrhash123"",""indexer"":""SonarrIndexer"",""downloadClient"":""Transmission"",""downloadUrl"":""https://example.com/s""}}]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Show S01E01"));
        Assert.That(result[0].DownloadId, Is.EqualTo("dl-123"));
        Assert.That(result[0].InfoHash, Is.EqualTo("sonarrhash123"));
        Assert.That(result[0].Indexer, Is.EqualTo("SonarrIndexer"));
        Assert.That(result[0].DownloadClient, Is.EqualTo("Transmission"));
        Assert.That(result[0].DownloadUrl, Is.EqualTo("https://example.com/s"));
    }

    [Test]
    public void GetDownloadHistory_should_skip_non_grabbed_events()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""downloadFolderImported"",""sourceTitle"":""Imported"",""downloadId"":""dl-1"",""date"":""2024-02-20T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Grabbed"",""downloadId"":""dl-2"",""date"":""2024-02-20T10:30:00Z"",""data"":{""torrentInfoHash"":""hash2""}}" +
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
        Assert.That(result[0].InfoHash, Is.EqualTo("hash2"));
    }

    [Test]
    public void GetDownloadHistory_should_skip_records_without_info_hash()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""grabbed"",""sourceTitle"":""No Hash"",""downloadId"":""dl-1"",""date"":""2024-02-20T10:00:00Z"",""data"":{}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Has Hash"",""downloadId"":""dl-2"",""date"":""2024-02-20T10:30:00Z"",""data"":{""torrentInfoHash"":""validhash""}}" +
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
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""No Data"",""downloadId"":""dl-1"",""date"":""2024-02-20T10:00:00Z""}]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_should_return_empty_when_non_success_status()
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

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetDownloadHistory_should_return_empty_when_no_records_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""page"":1}");

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
    public void TestConnection_should_return_true_when_status_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""version"":""4.0""}");

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
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Show"",""downloadId"":""dl-1"",""date"":""2024-02-20T10:00:00Z"",""data"":{""torrentInfoHash"":""""}}]}");

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
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Show S02E05"",""downloadId"":""dl-y"",""date"":""2024-03-10T08:00:00Z"",""data"":{""torrentInfoHash"":""tvhashval"",""indexer"":""TorrentLeech"",""downloadClient"":""Transmission"",""downloadUrl"":""https://tracker2.example.com/dl""}}]}");

        if (!InjectMockClient(handler))
        {
            Assert.Ignore("Cannot replace static readonly fields in this runtime");
            return;
        }

        _connection.Url = "http://localhost:19999";
        _connection.ApiKey = "test-key";

        var result = _connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Indexer, Is.EqualTo("TorrentLeech"));
        Assert.That(result[0].DownloadClient, Is.EqualTo("Transmission"));
        Assert.That(result[0].DownloadUrl, Is.EqualTo("https://tracker2.example.com/dl"));
    }

    [Test]
    public void GetDownloadHistory_should_use_utcnow_when_date_property_is_missing()
    {
        var before = DateTime.UtcNow;
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""data"":{""torrentInfoHash"":""nodatehash""}}]}");

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

    private static SonarrConnection CreateWithMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var policy = new ResiliencePipelineBuilder().Build();
        return new SonarrConnection(httpClient, policy);
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_parses_all_fields()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Show S01E01"",""downloadId"":""dl-abc"",""date"":""2024-02-20T14:00:00Z"",""data"":{""torrentInfoHash"":""sonarrhash"",""indexer"":""SonarrIdx"",""downloadClient"":""Transmission"",""downloadUrl"":""https://tracker.example.com/s""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8989";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Show S01E01"));
        Assert.That(result[0].DownloadId, Is.EqualTo("dl-abc"));
        Assert.That(result[0].InfoHash, Is.EqualTo("sonarrhash"));
        Assert.That(result[0].Indexer, Is.EqualTo("SonarrIdx"));
        Assert.That(result[0].DownloadClient, Is.EqualTo("Transmission"));
        Assert.That(result[0].DownloadUrl, Is.EqualTo("https://tracker.example.com/s"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_skips_non_grabbed_events()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""downloadFolderImported"",""sourceTitle"":""Imported"",""downloadId"":""dl-1"",""date"":""2024-02-20T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Grabbed Show"",""downloadId"":""dl-2"",""date"":""2024-02-20T10:30:00Z"",""data"":{""torrentInfoHash"":""hash2""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8989";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Title, Is.EqualTo("Grabbed Show"));
        Assert.That(result[0].InfoHash, Is.EqualTo("hash2"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_skips_records_without_info_hash()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[" +
            @"{""eventType"":""grabbed"",""sourceTitle"":""No Hash"",""downloadId"":""dl-1"",""date"":""2024-02-20T10:00:00Z"",""data"":{}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Has Hash"",""downloadId"":""dl-2"",""date"":""2024-02-20T10:30:00Z"",""data"":{""torrentInfoHash"":""validhash""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8989";
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
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""No Data"",""downloadId"":""dl-1"",""date"":""2024-02-20T10:00:00Z""}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8989";
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
        connection.Url = "http://localhost:8989";
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
        connection.Url = "http://localhost:8989";
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
        connection.Url = "http://localhost:8989";
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
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Show"",""downloadId"":""dl-1"",""date"":""2024-02-20T10:00:00Z"",""data"":{""torrentInfoHash"":""""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8989";
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
        connection.Url = "http://localhost:8989";
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
        connection.Url = "http://localhost:8989";
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
            @"{""eventType"":""grabbed"",""sourceTitle"":""Show S01E01"",""downloadId"":""dl-1"",""date"":""2024-02-20T10:00:00Z"",""data"":{""torrentInfoHash"":""hash1""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Show S01E02"",""downloadId"":""dl-2"",""date"":""2024-02-20T11:00:00Z"",""data"":{""torrentInfoHash"":""hash2""}}," +
            @"{""eventType"":""grabbed"",""sourceTitle"":""Show S01E03"",""downloadId"":""dl-3"",""date"":""2024-02-20T12:00:00Z"",""data"":{""torrentInfoHash"":""hash3""}}" +
            @"]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8989";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Title, Is.EqualTo("Show S01E01"));
        Assert.That(result[2].Title, Is.EqualTo("Show S01E03"));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_parses_date_correctly()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""records"":[{""eventType"":""grabbed"",""sourceTitle"":""Show"",""downloadId"":""dl-1"",""date"":""2024-03-10T09:15:00Z"",""data"":{""torrentInfoHash"":""tvhash999""}}]}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8989";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Date, Is.EqualTo(new DateTime(2024, 3, 10, 9, 15, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void GetDownloadHistory_with_injected_client_returns_empty_when_internal_server_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, @"{}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8989";
        connection.ApiKey = "test-key";

        var result = connection.GetDownloadHistory();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TestConnection_with_injected_client_returns_true_when_status_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""version"":""4.0""}");
        var connection = CreateWithMockClient(handler);
        connection.Url = "http://localhost:8989";
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
        connection.Url = "http://localhost:8989";
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
        connection.Url = "http://localhost:8989";
        connection.ApiKey = "test-key";

        var result = connection.TestConnection();

        Assert.That(result, Is.False);
    }
}
