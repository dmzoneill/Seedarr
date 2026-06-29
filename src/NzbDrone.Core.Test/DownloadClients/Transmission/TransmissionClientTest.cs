using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients.Transmission;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.DownloadClients.Transmission;

[TestFixture]
public class TransmissionClientTest
{
    private TransmissionClient _client;

    [SetUp]
    public void Setup()
    {
        _client = new TransmissionClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
    }

    private void InjectMockClient(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var field = typeof(TransmissionClient).GetField("_client",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(_client, httpClient);
    }

    [Test]
    public void Name_should_return_transmission()
    {
        Assert.That(_client.Name, Is.EqualTo("Transmission"));
    }

    [Test]
    public void ClientType_should_return_transmission()
    {
        Assert.That(_client.ClientType, Is.EqualTo("Transmission"));
    }

    [Test]
    public void Default_host_should_be_localhost()
    {
        Assert.That(_client.Host, Is.EqualTo("localhost"));
    }

    [Test]
    public void Default_port_should_be_9091()
    {
        Assert.That(_client.Port, Is.EqualTo(9091));
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
    public void Default_password_should_be_empty()
    {
        Assert.That(_client.Password, Is.EqualTo(""));
    }

    [Test]
    public void Default_category_should_be_empty()
    {
        Assert.That(_client.Category, Is.EqualTo(""));
    }

    [Test]
    public void RpcUrl_should_use_http_when_ssl_disabled()
    {
        _client.UseSsl = false;
        _client.Host = "myhost";
        _client.Port = 9091;

        var prop = typeof(TransmissionClient).GetProperty("RpcUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)prop.GetValue(_client);

        Assert.That(result, Is.EqualTo("http://myhost:9091/transmission/rpc"));
    }

    [Test]
    public void RpcUrl_should_use_https_when_ssl_enabled()
    {
        _client.UseSsl = true;
        _client.Host = "myhost";
        _client.Port = 443;

        var prop = typeof(TransmissionClient).GetProperty("RpcUrl",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (string)prop.GetValue(_client);

        Assert.That(result, Is.EqualTo("https://myhost:443/transmission/rpc"));
    }

    [Test]
    public void Host_should_be_settable()
    {
        _client.Host = "transmission.local";

        Assert.That(_client.Host, Is.EqualTo("transmission.local"));
    }

    [Test]
    public void Port_should_be_settable()
    {
        _client.Port = 12345;

        Assert.That(_client.Port, Is.EqualTo(12345));
    }

    [TestCase(0, "paused")]
    [TestCase(1, "checking")]
    [TestCase(2, "checking")]
    [TestCase(3, "downloading")]
    [TestCase(4, "downloading")]
    [TestCase(5, "seeding")]
    [TestCase(6, "seeding")]
    [TestCase(7, "unknown")]
    [TestCase(-1, "unknown")]
    [TestCase(99, "unknown")]
    public void MapStatus_should_return_correct_value(int status, string expected)
    {
        var method = typeof(TransmissionClient).GetMethod("MapStatus",
            BindingFlags.NonPublic | BindingFlags.Static);
        var result = (string)method.Invoke(null, new object[] { status });

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
    public void CreateRequest_should_create_post_request()
    {
        var method = typeof(TransmissionClient).GetMethod("CreateRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var request = (HttpRequestMessage)method.Invoke(_client, new object[] { "torrent-get", new { } });

        Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
        request.Dispose();
    }

    [Test]
    public void CreateRequest_should_set_rpc_url()
    {
        _client.Host = "test-host";
        _client.Port = 1234;
        _client.UseSsl = false;

        var method = typeof(TransmissionClient).GetMethod("CreateRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var request = (HttpRequestMessage)method.Invoke(_client, new object[] { "session-get", new { } });

        Assert.That(request.RequestUri.ToString(), Is.EqualTo("http://test-host:1234/transmission/rpc"));
        request.Dispose();
    }

    [Test]
    public void CreateRequest_should_include_basic_auth_when_username_set()
    {
        _client.Username = "admin";
        _client.Password = "secret";

        var method = typeof(TransmissionClient).GetMethod("CreateRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var request = (HttpRequestMessage)method.Invoke(_client, new object[] { "torrent-get", new { } });

        Assert.That(request.Headers.Authorization, Is.Not.Null);
        Assert.That(request.Headers.Authorization.Scheme, Is.EqualTo("Basic"));
        request.Dispose();
    }

    [Test]
    public void CreateRequest_should_not_include_auth_when_username_empty()
    {
        _client.Username = "";

        var method = typeof(TransmissionClient).GetMethod("CreateRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var request = (HttpRequestMessage)method.Invoke(_client, new object[] { "torrent-get", new { } });

        Assert.That(request.Headers.Authorization, Is.Null);
        request.Dispose();
    }

    [Test]
    public void CreateRequest_should_include_session_id_when_set()
    {
        var sessionField = typeof(TransmissionClient).GetField("_sessionId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        sessionField.SetValue(_client, "test-session-id");

        var method = typeof(TransmissionClient).GetMethod("CreateRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var request = (HttpRequestMessage)method.Invoke(_client, new object[] { "torrent-get", new { } });

        Assert.That(request.Headers.Contains("X-Transmission-Session-Id"), Is.True);
        request.Dispose();
    }

    [Test]
    public void CreateRequest_should_not_include_session_id_when_null()
    {
        var method = typeof(TransmissionClient).GetMethod("CreateRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var request = (HttpRequestMessage)method.Invoke(_client, new object[] { "torrent-get", new { } });

        Assert.That(request.Headers.Contains("X-Transmission-Session-Id"), Is.False);
        request.Dispose();
    }

    [Test]
    public void CreateRequest_should_set_json_content()
    {
        var method = typeof(TransmissionClient).GetMethod("CreateRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var request = (HttpRequestMessage)method.Invoke(_client, new object[] { "torrent-get", new { fields = new[] { "hash" } } });

        Assert.That(request.Content, Is.Not.Null);
        Assert.That(request.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
        request.Dispose();
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
    public void GetItems_should_parse_torrents_from_response()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[{""hashString"":""abc123"",""name"":""Test Torrent"",""totalSize"":1048576,""leftUntilDone"":512,""status"":6,""downloadDir"":""/downloads"",""labels"":[""cat1""]}]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("abc123"));
        Assert.That(result[0].Title, Is.EqualTo("Test Torrent"));
        Assert.That(result[0].TotalSize, Is.EqualTo(1048576));
        Assert.That(result[0].RemainingSize, Is.EqualTo(512));
        Assert.That(result[0].Status, Is.EqualTo("seeding"));
        Assert.That(result[0].OutputPath, Is.EqualTo("/downloads"));
        Assert.That(result[0].Category, Is.EqualTo("cat1"));
    }

    [Test]
    public void GetItems_should_return_multiple_torrents()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[" +
            @"{""hashString"":""hash1"",""name"":""Torrent 1"",""totalSize"":100,""leftUntilDone"":0,""status"":6,""downloadDir"":""/dl"",""labels"":[]}," +
            @"{""hashString"":""hash2"",""name"":""Torrent 2"",""totalSize"":200,""leftUntilDone"":50,""status"":4,""downloadDir"":""/dl2"",""labels"":[""x""]}" +
            @"]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].InfoHash, Is.EqualTo("hash1"));
        Assert.That(result[0].Status, Is.EqualTo("seeding"));
        Assert.That(result[0].Category, Is.EqualTo(""));
        Assert.That(result[1].InfoHash, Is.EqualTo("hash2"));
        Assert.That(result[1].Status, Is.EqualTo("downloading"));
        Assert.That(result[1].Category, Is.EqualTo("x"));
    }

    [Test]
    public void GetItems_should_filter_by_category_when_set()
    {
        _client.Category = "seedarr";
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[" +
            @"{""hashString"":""match"",""name"":""Matching"",""totalSize"":100,""leftUntilDone"":0,""status"":6,""downloadDir"":""/dl"",""labels"":[""seedarr""]}," +
            @"{""hashString"":""nomatch"",""name"":""No Match"",""totalSize"":200,""leftUntilDone"":0,""status"":6,""downloadDir"":""/dl"",""labels"":[""other""]}" +
            @"]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("match"));
    }

    [Test]
    public void GetItems_should_skip_torrents_without_matching_label()
    {
        _client.Category = "seedarr";
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[{""hashString"":""nolabel"",""name"":""No Label"",""totalSize"":100,""leftUntilDone"":0,""status"":6,""downloadDir"":""/dl""}]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_include_all_when_no_category()
    {
        _client.Category = "";
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[" +
            @"{""hashString"":""h1"",""name"":""T1"",""totalSize"":100,""leftUntilDone"":0,""status"":6,""downloadDir"":""/dl"",""labels"":[""a""]}," +
            @"{""hashString"":""h2"",""name"":""T2"",""totalSize"":200,""leftUntilDone"":0,""status"":0,""downloadDir"":""/dl"",""labels"":[""b""]}" +
            @"]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetItems_should_handle_missing_optional_properties()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[{}]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo(""));
        Assert.That(result[0].Title, Is.EqualTo(""));
        Assert.That(result[0].TotalSize, Is.EqualTo(0));
        Assert.That(result[0].RemainingSize, Is.EqualTo(0));
        Assert.That(result[0].Status, Is.EqualTo("paused"));
        Assert.That(result[0].OutputPath, Is.EqualTo(""));
        Assert.That(result[0].Category, Is.EqualTo(""));
    }

    [Test]
    public void GetItems_should_map_status_values_correctly()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[" +
            @"{""hashString"":""a"",""status"":0}," +
            @"{""hashString"":""b"",""status"":3}," +
            @"{""hashString"":""c"",""status"":5}," +
            @"{""hashString"":""d"",""status"":99}" +
            @"]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(4));
        Assert.That(result[0].Status, Is.EqualTo("paused"));
        Assert.That(result[1].Status, Is.EqualTo("downloading"));
        Assert.That(result[2].Status, Is.EqualTo("seeding"));
        Assert.That(result[3].Status, Is.EqualTo("unknown"));
    }

    [Test]
    public void GetTorrentFile_should_return_null_when_torrent_file_path_empty()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[{""torrentFile"":""""}]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetTorrentFile("abc123");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetTorrentFile_should_return_null_when_file_not_on_disk()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[{""torrentFile"":""/nonexistent/path/file.torrent""}]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetTorrentFile("abc123");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetTorrentFile_should_return_null_when_no_torrent_file_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[{""hashString"":""abc123""}]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetTorrentFile("abc123");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetTorrentFile_should_return_null_when_no_torrents()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetTorrentFile("abc123");

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestConnection_should_return_true_when_result_is_success()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":""success"",""arguments"":{}}");
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.True);
    }

    [Test]
    public void TestConnection_should_return_false_when_result_is_not_success()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":""error"",""arguments"":{}}");
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void TestConnection_should_return_false_when_no_result_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{}}");
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void SendRequest_should_retry_on_409_conflict_with_session_id()
    {
        var handler = new MockHttpMessageHandler();

        // First response: 409 Conflict with session ID header
        handler.EnqueueWithHeaders(
            HttpStatusCode.Conflict,
            @"{}",
            new Dictionary<string, string> { { "X-Transmission-Session-Id", "new-session-123" } });

        // Second response: 200 OK with valid data
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":""success"",""arguments"":{}}");

        InjectMockClient(handler);

        // TestConnection internally calls SendRequest, which handles 409 retry
        var result = _client.TestConnection();

        Assert.That(result, Is.True);

        // Verify the session ID was stored
        var sessionField = typeof(TransmissionClient).GetField("_sessionId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var sessionId = (string)sessionField.GetValue(_client);
        Assert.That(sessionId, Is.EqualTo("new-session-123"));
    }

    [Test]
    public void SendRequest_should_store_session_id_from_409_response()
    {
        var handler = new MockHttpMessageHandler();

        handler.EnqueueWithHeaders(
            HttpStatusCode.Conflict,
            @"{}",
            new Dictionary<string, string> { { "X-Transmission-Session-Id", "session-abc" } });
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[]},""result"":""success""}");

        InjectMockClient(handler);

        _client.GetItems();

        var sessionField = typeof(TransmissionClient).GetField("_sessionId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(sessionField.GetValue(_client), Is.EqualTo("session-abc"));
    }

    [Test]
    public void GetItems_should_use_first_label_as_category()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[{""hashString"":""h1"",""labels"":[""first"",""second"",""third""]}]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Category, Is.EqualTo("first"));
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
        _client.Category = "seedarr";

        Assert.That(_client.Category, Is.EqualTo("seedarr"));
    }

    [Test]
    public void Username_and_password_should_be_settable()
    {
        _client.Username = "user";
        _client.Password = "pass";

        Assert.That(_client.Username, Is.EqualTo("user"));
        Assert.That(_client.Password, Is.EqualTo("pass"));
    }

    [Test]
    public void GetTorrentFile_should_read_file_bytes_when_torrent_file_exists_on_disk()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            var expectedBytes = new byte[] { 0x64, 0x34, 0x3A, 0x69, 0x6E, 0x66, 0x6F, 0x65 };
            System.IO.File.WriteAllBytes(tempFile, expectedBytes);

            var handler = new MockHttpMessageHandler();
            handler.Enqueue(HttpStatusCode.OK,
                $@"{{""arguments"":{{""torrents"":[{{""torrentFile"":""{tempFile.Replace("\\", "\\\\")}""}}]}},""result"":""success""}}");
            InjectMockClient(handler);

            var result = _client.GetTorrentFile("abc123");

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(expectedBytes));
        }
        finally
        {
            System.IO.File.Delete(tempFile);
        }
    }

    [Test]
    public void SendRequest_should_retry_on_409_without_updating_session_when_no_session_header()
    {
        var handler = new MockHttpMessageHandler();

        // First response: 409 Conflict WITHOUT X-Transmission-Session-Id header
        handler.EnqueueWithHeaders(
            HttpStatusCode.Conflict,
            @"{}",
            new Dictionary<string, string>());

        // Second response: 200 OK
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":""success"",""arguments"":{}}");

        InjectMockClient(handler);

        // TestConnection triggers SendRequest
        var result = _client.TestConnection();

        Assert.That(result, Is.True);

        // Session ID should remain null since no header was provided
        var sessionField = typeof(TransmissionClient).GetField("_sessionId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var sessionId = (string)sessionField.GetValue(_client);
        Assert.That(sessionId, Is.Null.Or.Empty);
    }

    [Test]
    public void GetItems_should_return_empty_when_torrents_array_is_empty()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetItems_should_include_torrents_without_labels_when_no_category_filter()
    {
        _client.Category = "";
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK,
            @"{""arguments"":{""torrents"":[{""hashString"":""nolabels"",""name"":""No Labels"",""status"":6}]},""result"":""success""}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("nolabels"));
        Assert.That(result[0].Category, Is.EqualTo(""));
    }

    [Test]
    public void CreateRequest_should_encode_credentials_correctly()
    {
        _client.Username = "testuser";
        _client.Password = "testpass";

        var method = typeof(TransmissionClient).GetMethod("CreateRequest",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var request = (HttpRequestMessage)method.Invoke(_client, new object[] { "session-get", new { } });

        Assert.That(request.Headers.Authorization, Is.Not.Null);
        Assert.That(request.Headers.Authorization.Scheme, Is.EqualTo("Basic"));

        var decoded = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(request.Headers.Authorization.Parameter));
        Assert.That(decoded, Is.EqualTo("testuser:testpass"));
        request.Dispose();
    }
}
