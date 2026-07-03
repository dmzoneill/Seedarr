using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class TrackerServerTests : ApiTestBase
{
    private string _apiKey;

    [OneTimeSetUp]
    public async Task SetUpApiKey()
    {
        _apiKey = await GetApiKeyAsync(SeedarrUrl);
    }

    [Test]
    public async Task Tracker_server_stats_returns_object()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/stats", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task Tracker_server_stats_has_expected_fields()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/stats", _apiKey);
        using var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.TryGetProperty("totalTorrents", out _), Is.True, "Stats missing 'totalTorrents' property");
        Assert.That(doc.RootElement.TryGetProperty("totalPeers", out _), Is.True, "Stats missing 'totalPeers' property");
        Assert.That(doc.RootElement.TryGetProperty("uptime", out _), Is.True, "Stats missing 'uptime' property");
    }

    [Test]
    public async Task Tracker_server_stats_uptime_is_positive()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/stats", _apiKey);
        using var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.TryGetProperty("uptime", out var uptimeProp), Is.True, "Stats missing 'uptime' property");
        Assert.That(uptimeProp.GetInt64(), Is.GreaterThanOrEqualTo(0), "'uptime' must be >= 0");
    }

    [Test]
    public async Task Tracker_server_torrents_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/torrents", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Tracker_server_torrent_peers_returns_array()
    {
        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/torrents", _apiKey);
        using var listDoc = JsonDocument.Parse(listJson);

        if (listDoc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No tracked torrents available to test peer endpoint.");

        var infoHash = listDoc.RootElement[0].GetProperty("infoHash").GetString();

        var peersJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/trackerserver/torrents/{infoHash}/peers", _apiKey);
        using var peersDoc = JsonDocument.Parse(peersJson);
        Assert.That(peersDoc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }
}
