using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class HealthApiTests : ApiTestBase
{
    [Test]
    public async Task Health_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/health");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Health_items_have_source_field()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/health");
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            Assert.That(item.TryGetProperty("source", out _), Is.True, "Each health item should have a 'source' property");
        }
    }

    [Test]
    public async Task Health_endpoint_returns_200()
    {
        var response = await Client.GetAsync($"{SeedarrUrl}/api/v1/health");
        Assert.That((int)response.StatusCode, Is.EqualTo(200), "GET /api/v1/health should return HTTP 200");
    }
}
