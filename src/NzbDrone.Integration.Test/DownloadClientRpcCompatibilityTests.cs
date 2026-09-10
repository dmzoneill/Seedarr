using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class DownloadClientRpcCompatibilityTests : IntegrationTestBase
{
    [Test]
    public async Task QBittorrent_Version_Returns_200()
    {
        var response = await Client.GetAsync("/api/v2/app/version");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("v4.4.2"));
    }

    [Test]
    public async Task QBittorrent_WebapiVersion_Returns_200()
    {
        var response = await Client.GetAsync("/api/v2/app/webapiVersion");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Is.EqualTo("2.8.3"));
    }

    [Test]
    public async Task QBittorrent_TorrentsInfo_Returns_200_Array()
    {
        var response = await Client.GetAsync("/api/v2/torrents/info");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Deluge_JsonRpc_Version_Returns_200()
    {
        var payload = new
        {
            method = "core.get_version",
            @params = new object[] { },
            id = 1
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/json", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var respJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respJson);
        Assert.That(doc.RootElement.GetProperty("result").GetString(), Is.EqualTo("2.1.1"));
        Assert.That(doc.RootElement.GetProperty("id").GetInt64(), Is.EqualTo(1));
    }

    [Test]
    public async Task Deluge_JsonRpc_Batch_Returns_Array()
    {
        var batch = new object[]
        {
            new { method = "core.get_version", @params = new object[] { }, id = 1 },
            new { method = "system.listMethods", @params = new object[] { }, id = 2 }
        };

        var json = JsonSerializer.Serialize(batch);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/json", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var respJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respJson);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(doc.RootElement.GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task Transmission_Rpc_Without_Session_Returns_409_With_Header()
    {
        var payload = new
        {
            method = "session-get",
            tag = 123
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/transmission/rpc", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(response.Headers.Contains("X-Transmission-Session-Id"), Is.True);

        var sessionId = string.Join("", response.Headers.GetValues("X-Transmission-Session-Id"));

        using var requestMsg = new HttpRequestMessage(HttpMethod.Post, "/transmission/rpc")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        requestMsg.Headers.Add("X-Transmission-Session-Id", sessionId);

        var authenticatedResponse = await Client.SendAsync(requestMsg);
        Assert.That(authenticatedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var respJson = await authenticatedResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respJson);
        Assert.That(doc.RootElement.GetProperty("result").GetString(), Is.EqualTo("success"));
        Assert.That(doc.RootElement.GetProperty("tag").GetInt64(), Is.EqualTo(123));
    }
}
