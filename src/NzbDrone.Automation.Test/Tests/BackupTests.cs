using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class BackupTests : ApiTestBase
{
    private string _apiKey;

    [OneTimeSetUp]
    public async Task SetUpApiKey()
    {
        _apiKey = await GetApiKeyAsync(SeedarrUrl);
    }

    [Test]
    public async Task Backup_list_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/backup", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Create_backup_appears_in_list()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SeedarrUrl}/api/v1/backup")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        var response = await Client.SendAsync(request);
        Assert.That(
            response.IsSuccessStatusCode,
            Is.True,
            $"POST /api/v1/backup returned {(int)response.StatusCode}");

        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/backup", _apiKey);
        using var doc = JsonDocument.Parse(listJson);
        Assert.That(doc.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task Backup_has_name_and_size()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/backup", _apiKey);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.GetArrayLength() == 0)
            return;

        var first = doc.RootElement[0];
        Assert.That(first.TryGetProperty("name", out var nameProp), Is.True, "Backup missing 'name' property");
        Assert.That(nameProp.GetString(), Is.Not.Null.And.Not.Empty, "'name' must be a non-empty string");

        Assert.That(first.TryGetProperty("size", out var sizeProp), Is.True, "Backup missing 'size' property");
        Assert.That(sizeProp.GetInt64(), Is.GreaterThanOrEqualTo(0), "'size' must be >= 0");
    }

    [Test]
    public async Task Download_backup_returns_bytes()
    {
        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/backup", _apiKey);
        using var listDoc = JsonDocument.Parse(listJson);

        if (listDoc.RootElement.GetArrayLength() == 0)
            return;

        var id = listDoc.RootElement[0].GetProperty("id").GetInt32();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{SeedarrUrl}/api/v1/backup/{id}/download");
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        var response = await Client.SendAsync(request);
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
        Assert.That(response.Content.Headers.ContentLength, Is.GreaterThan(0));
    }

    [Test]
    public async Task Delete_backup_removes_it()
    {
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{SeedarrUrl}/api/v1/backup")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(_apiKey))
            createRequest.Headers.Add("X-Api-Key", _apiKey);

        var createResponse = await Client.SendAsync(createRequest);
        Assert.That(
            createResponse.IsSuccessStatusCode,
            Is.True,
            $"POST /api/v1/backup returned {(int)createResponse.StatusCode}");

        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/backup", _apiKey);
        using var listDoc = JsonDocument.Parse(listJson);
        Assert.That(
            listDoc.RootElement.GetArrayLength(),
            Is.GreaterThanOrEqualTo(1),
            "Backup list must not be empty after creation");

        var latestId = listDoc.RootElement[0].GetProperty("id").GetInt32();

        var deleted = await DeleteAsync($"{SeedarrUrl}/api/v1/backup/{latestId}");
        Assert.That(deleted, Is.True, $"DELETE /api/v1/backup/{latestId} did not succeed");

        var afterJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/backup", _apiKey);
        using var afterDoc = JsonDocument.Parse(afterJson);

        foreach (var item in afterDoc.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetInt32();
            Assert.That(id, Is.Not.EqualTo(latestId), $"Deleted backup id {latestId} still present in list");
        }
    }
}
