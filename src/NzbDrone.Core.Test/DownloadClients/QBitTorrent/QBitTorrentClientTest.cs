using System.Net;
using System.Net.Http;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients.QBitTorrent;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.DownloadClients.QBitTorrent;

[TestFixture]
public class QBitTorrentClientTest
{
    private QBitTorrentClient _client;

    [SetUp]
    public void Setup()
    {
        _client = new QBitTorrentClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
    }

    private void InjectMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var field = typeof(QBitTorrentClient).GetField("_client",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(_client, httpClient);
    }

    [Test]
    public void Name_should_return_qbittorrent()
    {
        Assert.That(_client.Name, Is.EqualTo("qBittorrent"));
    }

    [Test]
    public void ClientType_should_return_qbittorrent()
    {
        Assert.That(_client.ClientType, Is.EqualTo("QBitTorrent"));
    }

    [Test]
    public void Default_host_should_be_localhost()
    {
        Assert.That(_client.Host, Is.EqualTo("localhost"));
    }

    [Test]
    public void Default_port_should_be_8080()
    {
        Assert.That(_client.Port, Is.EqualTo(8080));
    }

    [Test]
    public void Default_use_ssl_should_be_false()
    {
        Assert.That(_client.UseSsl, Is.False);
    }

    [Test]
    public void Default_username_should_be_admin()
    {
        Assert.That(_client.Username, Is.EqualTo("admin"));
    }

    [Test]
    public void Default_password_should_be_adminadmin()
    {
        Assert.That(_client.Password, Is.EqualTo("adminadmin"));
    }

    [Test]
    public void Default_category_should_be_empty()
    {
        Assert.That(_client.Category, Is.EqualTo(""));
    }

    [Test]
    public void BaseUrl_should_use_http_when_ssl_disabled()
    {
        _client.UseSsl = false;
        _client.Host = "myhost";
        _client.Port = 8080;

        var prop = typeof(QBitTorrentClient).GetProperty("BaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)prop.GetValue(_client);

        Assert.That(result, Is.EqualTo("http://myhost:8080"));
    }

    [Test]
    public void BaseUrl_should_use_https_when_ssl_enabled()
    {
        _client.UseSsl = true;
        _client.Host = "secure-qbt";
        _client.Port = 443;

        var prop = typeof(QBitTorrentClient).GetProperty("BaseUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)prop.GetValue(_client);

        Assert.That(result, Is.EqualTo("https://secure-qbt:443"));
    }

    [Test]
    public void Host_should_be_settable()
    {
        _client.Host = "qbt.local";

        Assert.That(_client.Host, Is.EqualTo("qbt.local"));
    }

    [Test]
    public void Port_should_be_settable()
    {
        _client.Port = 7777;

        Assert.That(_client.Port, Is.EqualTo(7777));
    }

    [TestCase("uploading", "seeding")]
    [TestCase("stalledUP", "seeding")]
    [TestCase("forcedUP", "seeding")]
    [TestCase("queuedUP", "seeding")]
    [TestCase("downloading", "downloading")]
    [TestCase("stalledDL", "downloading")]
    [TestCase("forcedDL", "downloading")]
    [TestCase("queuedDL", "downloading")]
    [TestCase("pausedUP", "paused")]
    [TestCase("pausedDL", "paused")]
    [TestCase("checkingUP", "checking")]
    [TestCase("checkingDL", "checking")]
    [TestCase("checkingResumeData", "checking")]
    [TestCase("missingFiles", "unknown")]
    [TestCase("error", "unknown")]
    [TestCase("", "unknown")]
    public void MapState_should_return_correct_value(string state, string expected)
    {
        var method = typeof(QBitTorrentClient).GetMethod("MapState",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (string)method.Invoke(null, new object[] { state });

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Dispose_should_not_throw()
    {
        Assert.DoesNotThrow(() => _client.Dispose());
    }

    [Test]
    public void Dispose_should_be_idempotent()
    {
        _client.Dispose();
        Assert.DoesNotThrow(() => _client.Dispose());
    }

    [Test]
    public void GetItems_should_return_empty_list_when_connection_fails()
    {
        _client.Host = "nonexistent.invalid";
        _client.Port = 1;

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetTorrentFile_should_return_null_when_connection_fails()
    {
        _client.Host = "nonexistent.invalid";
        _client.Port = 1;

        var result = _client.GetTorrentFile("abc123");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestConnection_should_return_false_when_connection_fails()
    {
        _client.Host = "nonexistent.invalid";
        _client.Port = 1;

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    // --- Happy-path and branch coverage tests below ---

    [Test]
    public void GetItems_should_parse_torrents_when_authenticated()
    {
        var handler = new MockHttpMessageHandler();

        // Auth response
        handler.Enqueue(HttpStatusCode.OK, "Ok.");

        // Torrents response
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""hash"":""abc123"",""name"":""Test Torrent"",""total_size"":1048576,""amount_left"":0,""state"":""uploading"",""save_path"":""/downloads"",""category"":""seedarr""}]");

        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("abc123"));
        Assert.That(result[0].Title, Is.EqualTo("Test Torrent"));
        Assert.That(result[0].TotalSize, Is.EqualTo(1048576));
        Assert.That(result[0].RemainingSize, Is.EqualTo(0));
        Assert.That(result[0].Status, Is.EqualTo("seeding"));
        Assert.That(result[0].OutputPath, Is.EqualTo("/downloads"));
        Assert.That(result[0].Category, Is.EqualTo("seedarr"));
    }

    [Test]
    public void GetItems_should_return_multiple_torrents()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Ok.");
        handler.Enqueue(HttpStatusCode.OK,
            @"[" +
            @"{""hash"":""h1"",""name"":""T1"",""total_size"":100,""amount_left"":50,""state"":""downloading"",""save_path"":""/dl"",""category"":""cat1""}," +
            @"{""hash"":""h2"",""name"":""T2"",""total_size"":200,""amount_left"":0,""state"":""pausedUP"",""save_path"":""/dl2"",""category"":""cat2""}" +
            @"]");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].InfoHash, Is.EqualTo("h1"));
        Assert.That(result[0].Status, Is.EqualTo("downloading"));
        Assert.That(result[1].InfoHash, Is.EqualTo("h2"));
        Assert.That(result[1].Status, Is.EqualTo("paused"));
    }

    [Test]
    public void GetItems_should_return_empty_when_auth_fails()
    {
        var handler = new MockHttpMessageHandler();

        // Auth returns non-Ok body
        handler.Enqueue(HttpStatusCode.OK, "Fails.");

        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_return_empty_when_auth_returns_error_status()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Forbidden, "Forbidden");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_return_empty_when_torrents_response_not_success()
    {
        var handler = new MockHttpMessageHandler();

        // Auth succeeds
        handler.Enqueue(HttpStatusCode.OK, "Ok.");

        // Torrents endpoint returns error
        handler.Enqueue(HttpStatusCode.InternalServerError, "error");

        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_handle_missing_properties()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Ok.");
        handler.Enqueue(HttpStatusCode.OK, @"[{}]");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo(""));
        Assert.That(result[0].Title, Is.EqualTo(""));
        Assert.That(result[0].TotalSize, Is.EqualTo(0));
        Assert.That(result[0].RemainingSize, Is.EqualTo(0));
        Assert.That(result[0].Status, Is.EqualTo("unknown"));
        Assert.That(result[0].OutputPath, Is.EqualTo(""));
        Assert.That(result[0].Category, Is.EqualTo(""));
    }

    [Test]
    public void GetItems_should_map_various_states()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Ok.");
        handler.Enqueue(HttpStatusCode.OK,
            @"[" +
            @"{""hash"":""a"",""state"":""stalledDL""}," +
            @"{""hash"":""b"",""state"":""checkingUP""}," +
            @"{""hash"":""c"",""state"":""forcedUP""}," +
            @"{""hash"":""d"",""state"":""missingFiles""}" +
            @"]");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(4));
        Assert.That(result[0].Status, Is.EqualTo("downloading"));
        Assert.That(result[1].Status, Is.EqualTo("checking"));
        Assert.That(result[2].Status, Is.EqualTo("seeding"));
        Assert.That(result[3].Status, Is.EqualTo("unknown"));
    }

    [Test]
    public void GetTorrentFile_should_return_bytes_when_export_succeeds()
    {
        var handler = new MockHttpMessageHandler();

        // Auth response
        handler.Enqueue(HttpStatusCode.OK, "Ok.");

        // Export response with torrent data
        var torrentBytes = new byte[] { 0x64, 0x38, 0x3A, 0x61 };
        handler.EnqueueBytes(HttpStatusCode.OK, torrentBytes);

        InjectMockClient(handler);

        var result = _client.GetTorrentFile("abc123");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(torrentBytes));
    }

    [Test]
    public void GetTorrentFile_should_return_null_when_export_returns_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Ok.");
        handler.Enqueue(HttpStatusCode.NotFound, "not found");
        InjectMockClient(handler);

        var result = _client.GetTorrentFile("abc123");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetTorrentFile_should_return_null_when_auth_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Fails.");
        InjectMockClient(handler);

        var result = _client.GetTorrentFile("abc123");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestConnection_should_return_true_when_version_endpoint_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "v4.6.1");
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.True);
    }

    [Test]
    public void TestConnection_should_fallback_to_auth_when_version_not_success()
    {
        var handler = new MockHttpMessageHandler();

        // Version endpoint returns 403 (needs auth)
        handler.Enqueue(HttpStatusCode.Forbidden, "");

        // Auth endpoint succeeds
        handler.Enqueue(HttpStatusCode.OK, "Ok.");

        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.True);
    }

    [Test]
    public void TestConnection_should_return_false_when_version_fails_and_auth_fails()
    {
        var handler = new MockHttpMessageHandler();

        // Version endpoint returns 403
        handler.Enqueue(HttpStatusCode.Forbidden, "");

        // Auth endpoint also fails
        handler.Enqueue(HttpStatusCode.OK, "Fails.");

        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void UseSsl_should_be_settable()
    {
        _client.UseSsl = true;

        Assert.That(_client.UseSsl, Is.True);
    }

    [Test]
    public void Category_should_be_settable()
    {
        _client.Category = "movies";

        Assert.That(_client.Category, Is.EqualTo("movies"));
    }

    [Test]
    public void GetItems_should_append_category_filter_when_category_is_set()
    {
        _client.Category = "movies";
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Ok.");
        handler.Enqueue(HttpStatusCode.OK,
            @"[{""hash"":""abc"",""name"":""Movie"",""total_size"":500,""amount_left"":0,""state"":""uploading"",""save_path"":""/dl"",""category"":""movies""}]");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Category, Is.EqualTo("movies"));
    }

    [Test]
    public void GetItems_should_return_empty_when_torrents_json_is_malformed()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Ok.");
        handler.Enqueue(HttpStatusCode.OK, "this is not valid json {{{");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_return_empty_when_authenticated_but_no_torrents()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Ok.");
        handler.Enqueue(HttpStatusCode.OK, @"[]");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_map_all_seeding_states()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Ok.");
        handler.Enqueue(HttpStatusCode.OK,
            @"[" +
            @"{""hash"":""a"",""state"":""queuedUP""}," +
            @"{""hash"":""b"",""state"":""forcedDL""}," +
            @"{""hash"":""c"",""state"":""checkingResumeData""}" +
            @"]");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Status, Is.EqualTo("seeding"));
        Assert.That(result[1].Status, Is.EqualTo("downloading"));
        Assert.That(result[2].Status, Is.EqualTo("checking"));
    }

    [Test]
    public void GetTorrentFile_should_return_bytes_with_correct_hash_in_url()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "Ok.");
        var expectedBytes = new byte[] { 0x64, 0x31, 0x3A };
        handler.EnqueueBytes(HttpStatusCode.OK, expectedBytes);
        InjectMockClient(handler);

        var result = _client.GetTorrentFile("deadbeef123");

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.EqualTo(expectedBytes));
    }
}
