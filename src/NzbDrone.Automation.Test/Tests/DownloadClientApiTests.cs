using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class DownloadClientApiTests : ApiTestBase
{
    private string _apiKey;

    [OneTimeSetUp]
    public async Task SetUpApiKey()
    {
        _apiKey = await GetApiKeyAsync(SeedarrUrl);
    }

    private async Task<int> GetFirstClientIdOrIgnoreAsync()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/downloadclients", _apiKey);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No download clients configured.");
        return doc.RootElement[0].GetProperty("id").GetInt32();
    }

    [Test]
    public async Task Download_clients_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/downloadclients", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(doc.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task Download_client_has_required_fields()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/downloadclients", _apiKey);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No download clients configured.");

        var first = doc.RootElement[0];
        Assert.That(first.TryGetProperty("id", out _), Is.True, "Download client missing 'id' property");
        Assert.That(first.TryGetProperty("name", out _), Is.True, "Download client missing 'name' property");
        Assert.That(first.TryGetProperty("host", out _), Is.True, "Download client missing 'host' property");
    }

    [Test]
    public async Task Get_download_client_by_id_returns_client()
    {
        var firstId = await GetFirstClientIdOrIgnoreAsync();

        var itemJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/downloadclients/{firstId}", _apiKey);
        using var itemDoc = JsonDocument.Parse(itemJson);

        Assert.That(itemDoc.RootElement.TryGetProperty("id", out var idProp), Is.True);
        Assert.That(idProp.GetInt32(), Is.EqualTo(firstId));
    }

    [Test]
    public async Task Download_client_test_connection_passes()
    {
        var firstId = await GetFirstClientIdOrIgnoreAsync();

        var responseJson = await PostJsonAsync(
            $"{SeedarrUrl}/api/v1/downloadclients/{firstId}/test",
            new { },
            _apiKey);

        using var doc = JsonDocument.Parse(responseJson);
        Assert.That(
            doc.RootElement.TryGetProperty("success", out var successProp),
            Is.True,
            "Test response missing 'success' property");
        Assert.That(successProp.GetBoolean(), Is.True, "Download client test connection did not pass");
    }
}
