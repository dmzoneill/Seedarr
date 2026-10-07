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

    [TestCase(4)]
    [TestCase(3601)]
    [TestCase(-1)]
    [TestCase(2000000000)]
    public async Task PutBitTorrentConfig_with_invalid_timeout_returns_400(int timeout)
    {
        var body = new
        {
            id = 1,
            announceIntervalSeconds = 100,
            minAnnounceIntervalSeconds = 50,
            scrapeIntervalSeconds = 100,
            customScriptTimeoutSeconds = timeout
        };

        var response = await PutJsonAsync("/api/v1/config/bittorrent/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PutBitTorrentConfig_with_valid_timeout_returns_202()
    {
        var body = new
        {
            id = 1,
            announceIntervalSeconds = 100,
            minAnnounceIntervalSeconds = 50,
            scrapeIntervalSeconds = 100,
            customScriptTimeoutSeconds = 120
        };

        var response = await PutJsonAsync("/api/v1/config/bittorrent/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    [TestCase("relative/path/to/script.sh")]
    [TestCase("script.sh")]
    [TestCase("{\"path\": \"relative/script.sh\"}")]
    public async Task PutBitTorrentConfig_with_invalid_script_path_returns_400(string scriptPath)
    {
        var body = new
        {
            id = 1,
            announceIntervalSeconds = 100,
            minAnnounceIntervalSeconds = 50,
            scrapeIntervalSeconds = 100,
            customScriptTimeoutSeconds = 60,
            onDownloadCompleteScript = scriptPath
        };

        var response = await PutJsonAsync("/api/v1/config/bittorrent/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [TestCase("/usr/local/bin/on-complete.sh")]
    [TestCase("{\"path\": \"/usr/local/bin/on-complete.sh\", \"arguments\": \"--foo\"}")]
    public async Task PutBitTorrentConfig_with_valid_script_path_returns_202(string scriptPath)
    {
        var body = new
        {
            id = 1,
            announceIntervalSeconds = 100,
            minAnnounceIntervalSeconds = 50,
            scrapeIntervalSeconds = 100,
            customScriptTimeoutSeconds = 60,
            onDownloadCompleteScript = scriptPath
        };

        var response = await PutJsonAsync("/api/v1/config/bittorrent/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    [Test]
    public async Task PutSeedingConfig_with_invalid_seedGoalReachedAction_returns_400()
    {
        var body = new
        {
            id = 1,
            seedGoalReachedAction = "InvalidAction"
        };

        var response = await PutJsonAsync("/api/v1/config/seeding/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task PutPeerProtocolConfig_with_invalid_peer_contact_interval_returns_400(int peerContactIntervalSeconds)
    {
        var body = ValidPeerProtocolConfigBody(peerContactIntervalSeconds: peerContactIntervalSeconds);

        var response = await PutJsonAsync("/api/v1/config/peerprotocol/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task PutPeerProtocolConfig_with_invalid_udp_tracker_timeout_returns_400(int udpTrackerTimeoutSeconds)
    {
        var body = ValidPeerProtocolConfigBody(udpTrackerTimeoutSeconds: udpTrackerTimeoutSeconds);

        var response = await PutJsonAsync("/api/v1/config/peerprotocol/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task PutPeerProtocolConfig_with_invalid_http_tracker_timeout_returns_400(int httpTrackerTimeoutSeconds)
    {
        var body = ValidPeerProtocolConfigBody(httpTrackerTimeoutSeconds: httpTrackerTimeoutSeconds);

        var response = await PutJsonAsync("/api/v1/config/peerprotocol/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PutPeerProtocolConfig_with_valid_timeout_fields_returns_202()
    {
        var body = ValidPeerProtocolConfigBody();

        var response = await PutJsonAsync("/api/v1/config/peerprotocol/1", body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    }

    private static object ValidPeerProtocolConfigBody(
        int peerContactIntervalSeconds = 300,
        int udpTrackerTimeoutSeconds = 5,
        int httpTrackerTimeoutSeconds = 10)
    {
        return new
        {
            id = 1,
            handshakeTimeoutSeconds = 30,
            messageReadTimeoutSeconds = 60,
            keepAliveIntervalSeconds = 120,
            peerContactIntervalSeconds,
            udpTrackerTimeoutSeconds,
            httpTrackerTimeoutSeconds,
            peerRequestCount = 200,
            seederUploadActivityProbability = 0.85,
            peerIdleChance = 0.3,
            peerDropoutProbability = 0.1,
            connectionRotationPercentage = 0.25
        };
    }

    [Test]
    public async Task PutGeneralConfig_persists_theme_accent_and_language()
    {
        var body = new
        {
            id = 1,
            port = 9898,
            sslPort = 9899,
            watchFolderScanIntervalSeconds = 10,
            themeStyle = "indigo",
            colorScheme = "emerald",
            uiTheme = "indigo",
            uiAccent = "emerald",
            uiLanguage = "de"
        };
        var putResponse = await PutJsonAsync("/api/v1/config/general/1", body);
        Assert.That(putResponse.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var getResponse = await GetAsync("/api/v1/config/general");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await getResponse.Content.ReadAsStringAsync();
        var resource = Deserialize<Dictionary<string, object>>(json);

        Assert.That(resource["themeStyle"].ToString(), Is.EqualTo("indigo"));
        Assert.That(resource["colorScheme"].ToString(), Is.EqualTo("emerald"));
        Assert.That(resource["uiTheme"].ToString(), Is.EqualTo("indigo"));
        Assert.That(resource["uiAccent"].ToString(), Is.EqualTo("emerald"));
        Assert.That(resource["uiLanguage"].ToString(), Is.EqualTo("de"));
    }
}
