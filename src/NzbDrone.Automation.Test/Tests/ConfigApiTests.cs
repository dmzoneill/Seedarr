using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class ConfigApiTests : ApiTestBase
{
    [TestCase("general")]
    [TestCase("seeding")]
    [TestCase("network")]
    [TestCase("bittorrent")]
    [TestCase("peerprotocol")]
    [TestCase("protocols")]
    [TestCase("simulation")]
    [TestCase("trackerserver")]
    [TestCase("scheduler")]
    [TestCase("advanced")]
    public async Task Config_section_returns_id_1(string section)
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/config/{section}");
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        Assert.That(id, Is.EqualTo(1), $"config/{section} response should have id == 1");
    }

    [Test]
    public async Task Config_advanced_by_id_returns_singleton()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/config/advanced/1");
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        Assert.That(id, Is.EqualTo(1), "config/advanced/1 should return singleton with id == 1");
    }

    [Test]
    public async Task Config_advanced_put_round_trip()
    {
        var getJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/config/advanced");
        using var getDoc = JsonDocument.Parse(getJson);
        var origRate = getDoc.RootElement.GetProperty("uiRefreshRateSec").GetInt32();
        var newRate = origRate + 1;

        var putBody = new
        {
            id = 1,
            logToFile = true,
            fileLogLevel = "Info",
            debugMode = false,
            uiRefreshRateSec = newRate
        };

        var (statusCode, _) = await PutJsonAsync($"{SeedarrUrl}/api/v1/config/advanced/1", putBody);
        Assert.That(statusCode, Is.EqualTo(202), "PUT config/advanced/1 should return 202 Accepted");

        var updatedJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/config/advanced");
        using var updatedDoc = JsonDocument.Parse(updatedJson);
        var updatedRate = updatedDoc.RootElement.GetProperty("uiRefreshRateSec").GetInt32();
        Assert.That(updatedRate, Is.EqualTo(newRate), $"uiRefreshRateSec should be updated to {newRate}");

        var restoreBody = new
        {
            id = 1,
            logToFile = true,
            fileLogLevel = "Info",
            debugMode = false,
            uiRefreshRateSec = origRate
        };
        await PutJsonAsync($"{SeedarrUrl}/api/v1/config/advanced/1", restoreBody);
    }

    [Test]
    public async Task Config_seeding_put_round_trip()
    {
        var getJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/config/seeding");

        var origNode = JsonNode.Parse(getJson)!.AsObject();
        var origSpeed = origNode["maxUploadSpeedKbps"]!.GetValue<int>();

        var modifiedNode = JsonNode.Parse(getJson)!.AsObject();
        modifiedNode["maxUploadSpeedKbps"] = 12345;

        var (statusCode, _) = await PutJsonAsync($"{SeedarrUrl}/api/v1/config/seeding/1", modifiedNode);
        Assert.That(statusCode, Is.EqualTo(202), "PUT config/seeding/1 should return 202 Accepted");

        var updatedJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/config/seeding");
        using var updatedDoc = JsonDocument.Parse(updatedJson);
        var updatedSpeed = updatedDoc.RootElement.GetProperty("maxUploadSpeedKbps").GetInt32();
        Assert.That(updatedSpeed, Is.EqualTo(12345), "maxUploadSpeedKbps should be updated to 12345");

        var restoreNode = JsonNode.Parse(getJson)!.AsObject();
        await PutJsonAsync($"{SeedarrUrl}/api/v1/config/seeding/1", restoreNode);
    }

    [Test]
    public async Task Config_network_rejects_invalid_port()
    {
        var invalidBody = new
        {
            id = 1,
            listeningPort = 0,
            upnpEnabled = true,
            maxGlobalConnections = 200,
            maxPerTorrentConnections = 50,
            maxUploadSlots = 4,
            proxyType = "none",
            proxyHost = "",
            proxyPort = 8080,
            proxyAuthEnabled = false,
            proxyUsername = "",
            proxyPassword = ""
        };

        var (statusCode, _) = await PutJsonAsync($"{SeedarrUrl}/api/v1/config/network/1", invalidBody);
        Assert.That(statusCode, Is.EqualTo(400), "PUT config/network with listeningPort=0 should return 400 Bad Request");
    }
}
