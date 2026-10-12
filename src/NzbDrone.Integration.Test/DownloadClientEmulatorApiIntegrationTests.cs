using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class DownloadClientEmulatorApiIntegrationTests : IntegrationTestBase
{
    [Test]
    public async Task QBittorrent_ReadOnly_Endpoints_Return_200()
    {
        var paths = new[]
        {
            "/api/v2/app/version",
            "/api/v2/app/webapiVersion",
            "/api/v2/app/preferences",
            "/api/v2/app/defaultSavePath",
            "/api/v2/torrents/info",
            "/api/v2/torrents/categories",
            "/api/v2/torrents/tags",
            "/api/v2/sync/maindata?rid=0",
            "/api/v2/transfer/info",
            "/api/v2/transfer/speedLimitsMode",
        };

        foreach (var path in paths)
        {
            var response = await Client.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
        }
    }

    [Test]
    public async Task Deluge_Core_ReadOnly_Methods_Return_Results()
    {
        var methods = new[]
        {
            "core.get_version",
            "core.get_config",
            "core.get_config_values",
            "core.get_session_status",
            "core.get_free_space",
            "core.get_free_space_bytes",
            "core.get_enabled_plugins",
            "core.get_available_plugins",
            "daemon.get_version",
            "daemon.get_method_list",
            "daemon.info",
            "system.listMethods",
        };

        foreach (var method in methods)
        {
            var payload = new { method, @params = Array.Empty<object>(), id = 1 };
            var response = await PostDelugeAsync(payload);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), method);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.That(doc.RootElement.TryGetProperty("result", out _), Is.True, method);
            Assert.That(doc.RootElement.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null), method);
        }
    }

    [Test]
    public async Task Deluge_GetTorrentsStatus_Empty_Filter_Returns_Object()
    {
        var payload = new
        {
            method = "core.get_torrents_status",
            @params = new object[] { new { }, new[] { "name", "state" } },
            id = 42,
        };

        var response = await PostDelugeAsync(payload);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("result").ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task Transmission_Session_And_Torrent_Get_Succeed_With_Session_Header()
    {
        var sessionId = await GetTransmissionSessionIdAsync();

        var sessionGet = await PostTransmissionAsync(sessionId, new { method = "session-get", tag = 1 });
        Assert.That(sessionGet.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var torrentGet = await PostTransmissionAsync(sessionId, new
        {
            method = "torrent-get",
            arguments = new { fields = new[] { "id", "name", "status" } },
            tag = 2,
        });
        Assert.That(torrentGet.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var stats = await PostTransmissionAsync(sessionId, new { method = "session-stats", tag = 3 });
        Assert.That(stats.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var freeSpace = await PostTransmissionAsync(sessionId, new
        {
            method = "free-space",
            arguments = new { path = "/tmp" },
            tag = 4,
        });
        Assert.That(freeSpace.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var portTest = await PostTransmissionAsync(sessionId, new { method = "port-test", tag = 5 });
        Assert.That(portTest.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task<HttpResponseMessage> PostDelugeAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await Client.PostAsync("/json", content);
    }

    private async Task<string> GetTransmissionSessionIdAsync()
    {
        var payload = new { method = "session-get", tag = 99 };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/transmission/rpc", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(response.Headers.Contains("X-Transmission-Session-Id"), Is.True);
        return string.Join(string.Empty, response.Headers.GetValues("X-Transmission-Session-Id"));
    }

    private async Task<HttpResponseMessage> PostTransmissionAsync(string sessionId, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/transmission/rpc")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);
        return await Client.SendAsync(request);
    }
}
