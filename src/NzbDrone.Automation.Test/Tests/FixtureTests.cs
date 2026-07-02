using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class FixtureTests : ApiTestBase
{
    [Test]
    public async Task Test_torrent_fixture_served_via_http()
    {
        var response = await Client.GetAsync($"{SeedarrUrl}/fixtures/test.torrent");
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task Test_torrent_fixture_has_correct_size()
    {
        var response = await Client.GetAsync($"{SeedarrUrl}/fixtures/test.torrent");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.That(bytes.Length, Is.EqualTo(25829));
    }

    [Test]
    public async Task Transmission_web_ui_is_accessible()
    {
        var response = await Client.GetAsync($"{TransmissionUrl}/transmission/web/");
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task Sonarr_has_transmission_download_client()
    {
        var apiKey = await GetApiKeyAsync(SonarrUrl);
        if (string.IsNullOrEmpty(apiKey))
            Assert.Ignore("Sonarr API key not available");

        var json = await GetJsonAsync($"{SonarrUrl}/api/v3/downloadclient", apiKey);
        using var doc = JsonDocument.Parse(json);
        var count = 0;
        foreach (var client in doc.RootElement.EnumerateArray())
        {
            if (client.TryGetProperty("name", out var n) && n.GetString()?.Contains("Transmission") == true)
                count++;
        }

        Assert.That(count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task Radarr_has_transmission_download_client()
    {
        var apiKey = await GetApiKeyAsync(RadarrUrl);
        if (string.IsNullOrEmpty(apiKey))
            Assert.Ignore("Radarr API key not available");

        var json = await GetJsonAsync($"{RadarrUrl}/api/v3/downloadclient", apiKey);
        using var doc = JsonDocument.Parse(json);
        var count = 0;
        foreach (var client in doc.RootElement.EnumerateArray())
        {
            if (client.TryGetProperty("name", out var n) && n.GetString()?.Contains("Transmission") == true)
                count++;
        }

        Assert.That(count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task Lidarr_has_transmission_download_client()
    {
        var apiKey = await GetApiKeyAsync(LidarrUrl);
        if (string.IsNullOrEmpty(apiKey))
            Assert.Ignore("Lidarr API key not available");

        var json = await GetJsonAsync($"{LidarrUrl}/api/v1/downloadclient", apiKey);
        using var doc = JsonDocument.Parse(json);
        var count = 0;
        foreach (var client in doc.RootElement.EnumerateArray())
        {
            if (client.TryGetProperty("name", out var n) && n.GetString()?.Contains("Transmission") == true)
                count++;
        }

        Assert.That(count, Is.GreaterThanOrEqualTo(1));
    }
}
