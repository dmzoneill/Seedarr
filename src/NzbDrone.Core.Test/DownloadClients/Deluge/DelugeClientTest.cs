using System.Net;
using System.Net.Http;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients.Deluge;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.DownloadClients.Deluge;

[TestFixture]
public class DelugeClientTest
{
    private DelugeClient _client;

    [SetUp]
    public void Setup()
    {
        _client = new DelugeClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
    }

    private void InjectMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var field = typeof(DelugeClient).GetField("_client",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(_client, httpClient);
    }

    [Test]
    public void Name_should_return_deluge()
    {
        Assert.That(_client.Name, Is.EqualTo("Deluge"));
    }

    [Test]
    public void ClientType_should_return_deluge()
    {
        Assert.That(_client.ClientType, Is.EqualTo("Deluge"));
    }

    [Test]
    public void Default_host_should_be_localhost()
    {
        Assert.That(_client.Host, Is.EqualTo("localhost"));
    }

    [Test]
    public void Default_port_should_be_8112()
    {
        Assert.That(_client.Port, Is.EqualTo(8112));
    }

    [Test]
    public void Default_use_ssl_should_be_false()
    {
        Assert.That(_client.UseSsl, Is.False);
    }

    [Test]
    public void Default_username_should_be_empty()
    {
        Assert.That(_client.Username, Is.EqualTo(""));
    }

    [Test]
    public void Default_password_should_be_deluge()
    {
        Assert.That(_client.Password, Is.EqualTo("deluge"));
    }

    [Test]
    public void Default_category_should_be_empty()
    {
        Assert.That(_client.Category, Is.EqualTo(""));
    }

    [Test]
    public void JsonUrl_should_use_http_when_ssl_disabled()
    {
        _client.UseSsl = false;
        _client.Host = "myhost";
        _client.Port = 8112;

        var prop = typeof(DelugeClient).GetProperty("JsonUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)prop.GetValue(_client);

        Assert.That(result, Is.EqualTo("http://myhost:8112/json"));
    }

    [Test]
    public void JsonUrl_should_use_https_when_ssl_enabled()
    {
        _client.UseSsl = true;
        _client.Host = "secure-deluge";
        _client.Port = 443;

        var prop = typeof(DelugeClient).GetProperty("JsonUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)prop.GetValue(_client);

        Assert.That(result, Is.EqualTo("https://secure-deluge:443/json"));
    }

    [Test]
    public void Host_should_be_settable()
    {
        _client.Host = "deluge.local";

        Assert.That(_client.Host, Is.EqualTo("deluge.local"));
    }

    [Test]
    public void Port_should_be_settable()
    {
        _client.Port = 9999;

        Assert.That(_client.Port, Is.EqualTo(9999));
    }

    [TestCase("Seeding", "seeding")]
    [TestCase("Downloading", "downloading")]
    [TestCase("Paused", "paused")]
    [TestCase("Checking", "checking")]
    [TestCase("Queued", "downloading")]
    [TestCase("Error", "error")]
    [TestCase("SomethingElse", "unknown")]
    [TestCase("", "unknown")]
    public void MapState_should_return_correct_value(string state, string expected)
    {
        var method = typeof(DelugeClient).GetMethod("MapState",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (string)method.Invoke(null, new object[] { state });

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void GetTorrentFile_should_return_null()
    {
        var result = _client.GetTorrentFile("abc123");

        Assert.That(result, Is.Null);
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
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":true,""id"":0}");

        // update_ui response with torrents as an object keyed by hash
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{""abc123"":{""name"":""Test Torrent"",""total_size"":1048576,""total_remaining"":256,""state"":""Seeding"",""save_path"":""/downloads"",""label"":""seedarr""}}},""id"":1}");

        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("abc123"));
        Assert.That(result[0].Title, Is.EqualTo("Test Torrent"));
        Assert.That(result[0].TotalSize, Is.EqualTo(1048576));
        Assert.That(result[0].RemainingSize, Is.EqualTo(256));
        Assert.That(result[0].Status, Is.EqualTo("seeding"));
        Assert.That(result[0].OutputPath, Is.EqualTo("/downloads"));
        Assert.That(result[0].Category, Is.EqualTo("seedarr"));
    }

    [Test]
    public void GetItems_should_return_multiple_torrents()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{" +
            @"""hash1"":{""name"":""T1"",""total_size"":100,""total_remaining"":0,""state"":""Seeding"",""save_path"":""/dl1"",""label"":""a""}," +
            @"""hash2"":{""name"":""T2"",""total_size"":200,""total_remaining"":100,""state"":""Downloading"",""save_path"":""/dl2"",""label"":""b""}," +
            @"""hash3"":{""name"":""T3"",""total_size"":300,""total_remaining"":0,""state"":""Paused"",""save_path"":""/dl3"",""label"":""c""}" +
            @"}},""id"":1}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void GetItems_should_return_empty_when_auth_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":false,""id"":0}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_return_empty_when_no_result_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""id"":1}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_return_empty_when_no_torrents_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":{""stats"":{}},""id"":1}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_handle_missing_torrent_properties()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{""hash1"":{}}},""id"":1}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("hash1"));
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
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{" +
            @"""a"":{""state"":""Downloading""}," +
            @"""b"":{""state"":""Paused""}," +
            @"""c"":{""state"":""Checking""}," +
            @"""d"":{""state"":""Queued""}," +
            @"""e"":{""state"":""Error""}," +
            @"""f"":{""state"":""Unknown""}" +
            @"}},""id"":1}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(6));
        Assert.That(result[0].Status, Is.EqualTo("downloading"));
        Assert.That(result[1].Status, Is.EqualTo("paused"));
        Assert.That(result[2].Status, Is.EqualTo("checking"));
        Assert.That(result[3].Status, Is.EqualTo("downloading"));
        Assert.That(result[4].Status, Is.EqualTo("error"));
        Assert.That(result[5].Status, Is.EqualTo("unknown"));
    }

    [Test]
    public void TestConnection_should_return_true_when_auth_and_method_list_succeed()
    {
        var handler = new MockHttpMessageHandler();

        // Auth response
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");

        // daemon.get_method_list response
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":[""daemon.info"",""daemon.get_method_list""],""id"":1}");

        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.True);
    }

    [Test]
    public void TestConnection_should_return_false_when_auth_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":false,""id"":0}");
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void TestConnection_should_return_false_when_method_list_has_no_result()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""error"":""no method"",""id"":1}");
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetItems_should_handle_empty_torrents_object()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":{""torrents"":{}},""id"":1}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
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
        _client.Category = "tv";

        Assert.That(_client.Category, Is.EqualTo("tv"));
    }

    [Test]
    public void GetItems_should_add_label_filter_when_category_is_set()
    {
        _client.Category = "tv";
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{""tvhash"":{""name"":""Show S01E01"",""total_size"":700,""total_remaining"":0,""state"":""Seeding"",""save_path"":""/dl"",""label"":""tv""}}},""id"":1}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("tvhash"));
        Assert.That(result[0].Category, Is.EqualTo("tv"));
    }

    [Test]
    public void GetItems_should_catch_exception_when_update_ui_request_fails()
    {
        var handler = new MockHttpMessageHandler();

        // Auth succeeds; no second response queued so update_ui gets default 500 -> throws
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void TestConnection_should_return_false_when_daemon_method_list_request_fails()
    {
        var handler = new MockHttpMessageHandler();

        // Auth succeeds; no second response queued so daemon call gets default 500 -> throws
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetItems_should_return_empty_when_auth_http_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, @"{}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_handle_torrent_with_all_states()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{" +
            @"""s"":{""state"":""Seeding""}," +
            @"""d"":{""state"":""Downloading""}," +
            @"""q"":{""state"":""Queued""}" +
            @"}},""id"":1}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Status, Is.EqualTo("seeding"));
        Assert.That(result[1].Status, Is.EqualTo("downloading"));
        Assert.That(result[2].Status, Is.EqualTo("downloading")); // Queued maps to downloading
    }
}
