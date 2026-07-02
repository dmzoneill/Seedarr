using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class HealthTests : ApiTestBase
{
    [Test]
    public async Task Seedarr_api_is_healthy()
    {
        var response = await Client.GetAsync($"{SeedarrUrl}/api/v1/system/status");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Seedarr /api/v1/system/status returned {(int)response.StatusCode}");
    }

    [Test]
    public async Task Sonarr_is_healthy()
    {
        var response = await Client.GetAsync($"{SonarrUrl}/ping");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Sonarr /ping returned {(int)response.StatusCode}");
    }

    [Test]
    public async Task Radarr_is_healthy()
    {
        var response = await Client.GetAsync($"{RadarrUrl}/ping");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Radarr /ping returned {(int)response.StatusCode}");
    }

    [Test]
    public async Task Lidarr_is_healthy()
    {
        var response = await Client.GetAsync($"{LidarrUrl}/ping");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Lidarr /ping returned {(int)response.StatusCode}");
    }

    [Test]
    public async Task Prowlarr_is_healthy()
    {
        var response = await Client.GetAsync($"{ProwlarrUrl}/ping");
        Assert.That(response.IsSuccessStatusCode, Is.True, $"Prowlarr /ping returned {(int)response.StatusCode}");
    }
}
