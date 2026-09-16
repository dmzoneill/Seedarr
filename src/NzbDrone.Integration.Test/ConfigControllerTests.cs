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

    [Test]
    public async Task PutBitTorrentConfig_with_min_greater_than_announce_returns_400()
    {
        var body = new
        {
            id = 1,
            announceIntervalSeconds = 100,
            minAnnounceIntervalSeconds = 200,
            scrapeIntervalSeconds = 100
        };

        var response = await PutJsonAsync("/api/v1/config/bittorrent/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PutSeedingConfig_with_invalid_percentage_returns_400()
    {
        var body = new
        {
            id = 1,
            maxUploadSpeedKbps = 100,
            maxDownloadSpeedKbps = 100,
            altUploadSpeedKbps = 50,
            altDownloadSpeedKbps = 50,
            uploadDistributionSpreadPercentage = 150 // Invalid > 100
        };

        var response = await PutJsonAsync("/api/v1/config/seeding/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PutTrackerServerConfig_with_peers_exceeding_200_returns_400()
    {
        var body = new
        {
            id = 1,
            trackerHttpPort = 6969,
            trackerUdpPort = 6969,
            trackerAnnounceInterval = 1800,
            trackerMaxPeersPerAnnounce = 201
        };

        var response = await PutJsonAsync("/api/v1/config/trackerserver/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PutTrackerServerConfig_with_announce_interval_exceeding_86400_returns_400()
    {
        var body = new
        {
            id = 1,
            trackerHttpPort = 6969,
            trackerUdpPort = 6969,
            trackerAnnounceInterval = 86401,
            trackerMaxPeersPerAnnounce = 50
        };

        var response = await PutJsonAsync("/api/v1/config/trackerserver/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PutTrackerServerConfig_with_valid_config_returns_202()
    {
        var body = new
        {
            id = 1,
            trackerHttpPort = 6969,
            trackerUdpPort = 6969,
            trackerAnnounceInterval = 1800,
            trackerMaxPeersPerAnnounce = 50
        };

        var response = await PutJsonAsync("/api/v1/config/trackerserver/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    [Test]
    public async Task PutSimulationConfig_with_invalid_traffic_profile_returns_400()
    {
        var body = new
        {
            id = 1,
            trafficPatternProfile = "invalid_profile_xyz",
            primaryClient = "qbittorrent",
            swarmPeerAnalysisDepth = 50
        };

        var response = await PutJsonAsync("/api/v1/config/simulation/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PutSimulationConfig_with_invalid_primary_client_returns_400()
    {
        var body = new
        {
            id = 1,
            trafficPatternProfile = "balanced",
            primaryClient = "fake_client_123",
            swarmPeerAnalysisDepth = 50
        };

        var response = await PutJsonAsync("/api/v1/config/simulation/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PutSimulationConfig_with_depth_out_of_range_returns_400()
    {
        var body = new
        {
            id = 1,
            trafficPatternProfile = "balanced",
            primaryClient = "qbittorrent",
            swarmPeerAnalysisDepth = 9999
        };

        var response = await PutJsonAsync("/api/v1/config/simulation/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PutSimulationConfig_with_valid_parameters_returns_202()
    {
        var body = new
        {
            id = 1,
            trafficPatternProfile = "conservative",
            primaryClient = "qbittorrent",
            swarmPeerAnalysisDepth = 100
        };

        var response = await PutJsonAsync("/api/v1/config/simulation/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }
}
