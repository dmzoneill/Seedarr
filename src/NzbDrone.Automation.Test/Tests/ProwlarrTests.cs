using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class ProwlarrTests : ApiTestBase
{
    private string _prowlarrApiKey = string.Empty;

    [OneTimeSetUp]
    public async Task FetchProwlarrApiKey()
    {
        _prowlarrApiKey = await GetApiKeyAsync(ProwlarrUrl);
    }

    [Test]
    public async Task Prowlarr_api_is_reachable_with_valid_key()
    {
        if (string.IsNullOrEmpty(_prowlarrApiKey))
            Assert.Ignore("Prowlarr API key not available");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ProwlarrUrl}/api/v1/health");
        request.Headers.Add("X-Api-Key", _prowlarrApiKey);
        var response = await Client.SendAsync(request);
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task Prowlarr_has_3_apps_configured()
    {
        if (string.IsNullOrEmpty(_prowlarrApiKey))
            Assert.Ignore("Prowlarr API key not available");

        var json = await GetJsonAsync($"{ProwlarrUrl}/api/v1/applications", _prowlarrApiKey);
        using var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.GetArrayLength();
        Assert.That(count, Is.GreaterThanOrEqualTo(3), $"Expected at least 3 Prowlarr apps but found {count}");
    }
}
