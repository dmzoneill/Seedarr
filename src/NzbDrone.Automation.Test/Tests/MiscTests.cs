using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class MiscTests : ApiTestBase
{
    private string _apiKey;

    [OneTimeSetUp]
    public async Task SetUpApiKey()
    {
        _apiKey = await GetApiKeyAsync(SeedarrUrl);
    }

    [Test]
    public async Task Diskspace_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/diskspace", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Diskspace_has_path_and_free_space()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/diskspace", _apiKey);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetArrayLength() == 0)
            return;

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            Assert.That(entry.TryGetProperty("path", out var path), Is.True, "Diskspace entry missing path property");
            Assert.That(path.GetString(), Is.Not.Null.And.Not.Empty, "path must be a non-empty string");

            Assert.That(entry.TryGetProperty("freeSpace", out var freeSpace), Is.True, "Diskspace entry missing freeSpace property");
            Assert.That(freeSpace.GetInt64(), Is.GreaterThanOrEqualTo(0L), "freeSpace must be >= 0");
        }
    }

    [Test]
    public async Task Network_status_returns_object()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/network/status", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task Network_addresses_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/network/addresses", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Arr_sync_returns_result()
    {
        var json = await PostJsonAsync($"{SeedarrUrl}/api/v1/arrconnections/sync", new { }, _apiKey);
        Assert.That(json, Is.Not.Null.And.Not.Empty);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.Not.EqualTo(JsonValueKind.Undefined));
    }
}
