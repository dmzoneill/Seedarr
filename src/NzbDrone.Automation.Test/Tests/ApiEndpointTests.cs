using System;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class ApiEndpointTests : ApiTestBase
{
    private string _apiKey;

    [OneTimeSetUp]
    public async Task SetUpApiKey()
    {
        _apiKey = await GetApiKeyAsync(SeedarrUrl);
    }

    [Test]
    public async Task ArrConnections_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/arrconnections", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Indexers_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/indexers", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Torrent_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task At_least_3_arr_connections_configured()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/arrconnections", _apiKey);
        using var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.GetArrayLength();
        Assert.That(count, Is.GreaterThanOrEqualTo(3), $"Expected at least 3 arr connections but found {count}");
    }

    [Test]
    public async Task At_least_1_indexer_configured()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/indexers", _apiKey);
        using var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.GetArrayLength();
        Assert.That(count, Is.GreaterThanOrEqualTo(1), $"Expected at least 1 indexer but found {count}");
    }

    [Test]
    public async Task First_indexer_test_passes()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/indexers", _apiKey);
        using var indexersDoc = JsonDocument.Parse(json);
        if (indexersDoc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No indexers configured; skipping test.");

        var firstId = indexersDoc.RootElement[0].GetProperty("id").GetInt32();
        var resultJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/indexers/{firstId}/test", new { }, _apiKey);
        using var resultDoc = JsonDocument.Parse(resultJson);
        var success = resultDoc.RootElement.GetProperty("success").GetBoolean();
        Assert.That(success, Is.True, "Indexer test returned success=false");
    }

    [Test]
    public async Task Sonarr_connection_test_passes()
        => await ArrConnectionTestPasses("Sonarr");

    [Test]
    public async Task Radarr_connection_test_passes()
        => await ArrConnectionTestPasses("Radarr");

    [Test]
    public async Task Lidarr_connection_test_passes()
        => await ArrConnectionTestPasses("Lidarr");

    private async Task ArrConnectionTestPasses(string arrType)
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/arrconnections", _apiKey);
        using var connsDoc = JsonDocument.Parse(json);

        int? connId = null;
        foreach (var conn in connsDoc.RootElement.EnumerateArray())
        {
            if (conn.TryGetProperty("arrType", out var typeProp) &&
                string.Equals(typeProp.GetString(), arrType, StringComparison.OrdinalIgnoreCase))
            {
                connId = conn.GetProperty("id").GetInt32();
                break;
            }
        }

        if (connId is null)
            Assert.Ignore($"No {arrType} connection configured; skipping test.");

        var resultJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/arrconnections/{connId}/test", new { }, _apiKey);
        using var resultDoc = JsonDocument.Parse(resultJson);
        var success = resultDoc.RootElement.GetProperty("success").GetBoolean();
        Assert.That(success, Is.True, $"{arrType} connection test returned success=false");
    }
}
