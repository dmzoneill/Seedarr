using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.DownloadClients.Deluge;
using NzbDrone.Core.RemotePathMappings;
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
    public void GetItems_should_throw_when_connection_fails()
    {
        _client.Host = "nonexistent.invalid";
        _client.Port = 1;

        Assert.Throws<DownloadClientUnavailableException>(() => _client.GetItems());
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

        // web.connected check
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":true,""id"":1}");

        // update_ui response with torrents as an object keyed by hash
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{""abc123"":{""name"":""Test Torrent"",""total_size"":1048576,""total_remaining"":256,""state"":""Seeding"",""save_path"":""/downloads"",""label"":""seedarr""}}},""id"":2}");

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
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{" +
            @"""hash1"":{""name"":""T1"",""total_size"":100,""total_remaining"":0,""state"":""Seeding"",""save_path"":""/dl1"",""label"":""a""}," +
            @"""hash2"":{""name"":""T2"",""total_size"":200,""total_remaining"":100,""state"":""Downloading"",""save_path"":""/dl2"",""label"":""b""}," +
            @"""hash3"":{""name"":""T3"",""total_size"":300,""total_remaining"":0,""state"":""Paused"",""save_path"":""/dl3"",""label"":""c""}" +
            @"}},""id"":2}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(3));
    }

    [Test]
    public void GetItems_should_throw_when_auth_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":false,""id"":0}");
        InjectMockClient(handler);

        Assert.Throws<DownloadClientAuthenticationException>(() => _client.GetItems());
    }

    [Test]
    public void GetItems_should_return_empty_when_no_result_property()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(HttpStatusCode.OK, @"{""id"":2}");
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
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":{""stats"":{}},""id"":2}");
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
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{""hash1"":{}}},""id"":2}");
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
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{" +
            @"""a"":{""state"":""Downloading""}," +
            @"""b"":{""state"":""Paused""}," +
            @"""c"":{""state"":""Checking""}," +
            @"""d"":{""state"":""Queued""}," +
            @"""e"":{""state"":""Error""}," +
            @"""f"":{""state"":""Unknown""}" +
            @"}},""id"":2}");
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

        // web.connected check
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");

        // daemon.get_method_list response
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":[""daemon.info"",""daemon.get_method_list""],""id"":2}");

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
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(HttpStatusCode.OK, @"{""error"":""no method"",""id"":2}");
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetItems_should_handle_empty_torrents_object()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":{""torrents"":{}},""id"":2}");
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
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{""tvhash"":{""name"":""Show S01E01"",""total_size"":700,""total_remaining"":0,""state"":""Seeding"",""save_path"":""/dl"",""label"":""tv""}}},""id"":2}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InfoHash, Is.EqualTo("tvhash"));
        Assert.That(result[0].Category, Is.EqualTo("tv"));
    }

    [Test]
    public void GetItems_should_throw_when_update_ui_request_fails()
    {
        var handler = new MockHttpMessageHandler();

        // Auth succeeds
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        InjectMockClient(handler);

        Assert.Throws<DownloadClientUnavailableException>(() => _client.GetItems());
    }

    [Test]
    public void TestConnection_should_return_false_when_daemon_method_list_request_fails()
    {
        var handler = new MockHttpMessageHandler();

        // Auth succeeds
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetItems_should_throw_when_auth_http_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, @"{}");
        InjectMockClient(handler);

        Assert.Throws<DownloadClientAuthenticationException>(() => _client.GetItems());
    }

    [Test]
    public void GetItems_should_handle_torrent_with_all_states()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(HttpStatusCode.OK,
            @"{""result"":{""torrents"":{" +
            @"""s"":{""state"":""Seeding""}," +
            @"""d"":{""state"":""Downloading""}," +
            @"""q"":{""state"":""Queued""}" +
            @"}},""id"":2}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0].Status, Is.EqualTo("seeding"));
        Assert.That(result[1].Status, Is.EqualTo("downloading"));
        Assert.That(result[2].Status, Is.EqualTo("downloading")); // Queued maps to downloading
    }

    [Test]
    public void GetItems_should_extract_is_private_flag_correctly()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}");
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}");
        handler.Enqueue(
            HttpStatusCode.OK,
            @"{""result"":{""torrents"":{" +
            @"""priv1"":{""name"":""Private"",""total_size"":1000,""total_remaining"":0,""state"":""Seeding"",""private"":true}," +
            @"""pub1"":{""name"":""Public"",""total_size"":2000,""total_remaining"":0,""state"":""Seeding"",""private"":false}" +
            @"}},""id"":2}");
        InjectMockClient(handler);

        var result = _client.GetItems();

        Assert.That(result, Has.Count.EqualTo(2));
        var priv = result.Find(i => i.InfoHash == "priv1");
        var pub = result.Find(i => i.InfoHash == "pub1");
        Assert.That(priv, Is.Not.Null);
        Assert.That(priv.IsPrivate, Is.True);
        Assert.That(pub, Is.Not.Null);
        Assert.That(pub.IsPrivate, Is.False);
    }

    [Test]
    public async System.Threading.Tasks.Task GetSpeedLimitsAsync_should_return_parsed_speed_limits()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}"); // login
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}"); // web.connected
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":{""max_upload_speed"":500.0,""max_download_speed"":1000.0},""id"":2}"); // core.get_config
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":{""upload_rate"":102400.0,""download_rate"":204800.0},""id"":3}"); // core.get_session_status
        InjectMockClient(handler);

        var result = await _client.GetSpeedLimitsAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.UploadLimitBps, Is.EqualTo((long)(500.0 * 1024)));
        Assert.That(result.DownloadLimitBps, Is.EqualTo((long)(1000.0 * 1024)));
        Assert.That(result.CurrentUploadRateBps, Is.EqualTo(102400));
        Assert.That(result.CurrentDownloadRateBps, Is.EqualTo(204800));
    }

    [Test]
    public async System.Threading.Tasks.Task SetSpeedLimitsAsync_should_send_request()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}"); // login
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}"); // web.connected
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":2}"); // core.set_config
        InjectMockClient(handler);

        await _client.SetSpeedLimitsAsync(500 * 1024, 1000 * 1024);

        Assert.That(handler.Requests, Has.Count.EqualTo(3));
    }

    [Test]
    public async System.Threading.Tasks.Task SetTorrentLimitsAsync_should_send_request()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}"); // login
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}"); // web.connected
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":2}"); // core.set_torrent_options
        InjectMockClient(handler);

        await _client.SetTorrentLimitsAsync("hash123", 250 * 1024, 500 * 1024);

        Assert.That(handler.Requests, Has.Count.EqualTo(3));
    }

    [Test]
    public void Authenticate_should_query_hosts_and_connect_when_web_disconnected()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}"); // login
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":false,""id"":1}"); // web.connected (false)
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":[[""host-id-123"",""127.0.0.1"",58846,""Online""]],""id"":2}"); // web.get_hosts
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":3}"); // web.connect
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":4}"); // web.connected (true)
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":[""daemon.info""],""id"":5}"); // daemon.get_method_list
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.True);
    }

    [Test]
    public void Authenticate_should_return_false_when_web_connect_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}"); // login
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":false,""id"":1}"); // web.connected (false)
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":[[""host-id-123"",""127.0.0.1"",58846,""Offline""]],""id"":2}"); // web.get_hosts
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void Authenticate_should_return_false_when_post_connect_still_disconnected()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}"); // login
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":false,""id"":1}"); // web.connected (false)
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":[[""host-id-123"",""127.0.0.1"",58846,""Online""]],""id"":2}"); // web.get_hosts
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":3}"); // web.connect
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":false,""id"":4}"); // web.connected (still false)
        InjectMockClient(handler);

        var result = _client.TestConnection();

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetTorrentFile_should_return_bytes_when_local_torrent_directory_configured()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var hash = "aabbccdd112233445566";
            var filePath = Path.Combine(tempDir, $"{hash}.torrent");
            var expectedBytes = new byte[] { 1, 2, 3, 4, 5 };
            File.WriteAllBytes(filePath, expectedBytes);

            _client.LocalTorrentDirectory = tempDir;

            var result = _client.GetTorrentFile(hash);

            Assert.That(result, Is.EqualTo(expectedBytes));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void GetTorrentFile_should_remap_using_remote_path_mapping_service()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var hash = "torrent123";
            var localPath = Path.Combine(tempDir, $"{hash}.torrent");
            var expectedBytes = new byte[] { 9, 8, 7, 6 };
            File.WriteAllBytes(localPath, expectedBytes);

            var remotePath = $"/var/lib/deluge/state/{hash}.torrent";
            var mappingService = Substitute.For<IRemotePathMappingService>();
            mappingService.Remap(_client.Host, remotePath).Returns(localPath);

            var handler = new MockHttpMessageHandler();
            handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}"); // login
            handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}"); // web.connected
            handler.Enqueue(HttpStatusCode.OK,
                $@"{{""result"":{{""torrent_file"":""{remotePath}""}},""id"":2}}"); // core.get_torrent_status
            InjectMockClient(handler);

            _client.RemotePathMappingService = mappingService;

            var result = _client.GetTorrentFile(hash);

            Assert.That(result, Is.EqualTo(expectedBytes));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void GetTorrentFile_should_handle_missing_torrent_gracefully_without_throwing()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":0}"); // login
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":true,""id"":1}"); // web.connected
        handler.Enqueue(HttpStatusCode.OK, @"{""result"":null,""id"":2}"); // core.get_torrent_status
        InjectMockClient(handler);

        var result = _client.GetTorrentFile("nonexistent123");

        Assert.That(result, Is.Null);
    }
}
