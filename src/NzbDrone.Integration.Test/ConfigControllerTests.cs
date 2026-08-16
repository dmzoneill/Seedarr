using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class ConfigControllerTests : IntegrationTestBase
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
    public async Task GetConfig_returns_200_with_id1(string section)
    {
        var response = await GetAsync($"/api/v1/config/{section}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var resource = Deserialize<Dictionary<string, object>>(json);

        Assert.That(resource.ContainsKey("id"), Is.True);
        Assert.That(resource["id"].ToString(), Is.EqualTo("1"));
    }

    [Test]
    public async Task PutAdvancedConfig_returns_202()
    {
        var body = new { id = 1, uiRefreshRateSec = 99 };
        var response = await PutJsonAsync("/api/v1/config/advanced/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    [Test]
    public async Task PutAdvancedConfig_persists_uiRefreshRateSec()
    {
        var body = new { id = 1, uiRefreshRateSec = 42 };
        var putResponse = await PutJsonAsync("/api/v1/config/advanced/1", body);
        Assert.That(putResponse.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var getResponse = await GetAsync("/api/v1/config/advanced");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await getResponse.Content.ReadAsStringAsync();
        var resource = Deserialize<Dictionary<string, object>>(json);

        Assert.That(resource["uiRefreshRateSec"].ToString(), Is.EqualTo("42"));
    }

    [Test]
    public async Task PutNetworkConfig_with_invalid_port_returns_400()
    {
        var body = new
        {
            id = 1,
            listeningPort = 0,
            maxGlobalConnections = 200,
            maxPerTorrentConnections = 50,
            maxUploadSlots = 4,
            proxyPort = 8080
        };

        var response = await PutJsonAsync("/api/v1/config/network/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
