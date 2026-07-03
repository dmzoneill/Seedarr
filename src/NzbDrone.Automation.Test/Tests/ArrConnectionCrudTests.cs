using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class ArrConnectionCrudTests : ApiTestBase
{
    private string _apiKey;

    private static readonly object CreatePayload = new
    {
        name = "TestConn-Integration",
        arrType = "Sonarr",
        url = "http://localhost:19999",
        apiKey = "testkey-not-real-00000000",
        implementation = "SonarrConnection",
        configContract = "ArrConnectionDefinition",
        enable = false,
        webhookEnabled = false,
        syncEnabled = false
    };

    [OneTimeSetUp]
    public async Task SetUpApiKey()
    {
        _apiKey = await GetApiKeyAsync(SeedarrUrl);
    }

    [SetUp]
    public async Task SetUp()
    {
        await CleanupTestConnectionsAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await CleanupTestConnectionsAsync();
    }

    private async Task CleanupTestConnectionsAsync()
    {
        try
        {
            var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/arrconnections", _apiKey);
            using var doc = JsonDocument.Parse(json);

            foreach (var conn in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var name = conn.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() ?? string.Empty
                        : string.Empty;

                    if (name.StartsWith("TestConn-"))
                    {
                        var id = conn.GetProperty("id").GetInt32();
                        await DeleteAsync($"{SeedarrUrl}/api/v1/arrconnections/{id}");
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private async Task<int> CreateTestConnectionAsync()
    {
        var json = await PostJsonAsync($"{SeedarrUrl}/api/v1/arrconnections", CreatePayload, _apiKey);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    [Test]
    public async Task Arr_connections_list_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/arrconnections", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(doc.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task Create_arr_connection_returns_created()
    {
        var responseJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/arrconnections", CreatePayload, _apiKey);
        using var doc = JsonDocument.Parse(responseJson);

        var id = doc.RootElement.GetProperty("id").GetInt32();
        var name = doc.RootElement.GetProperty("name").GetString();

        Assert.That(id, Is.GreaterThan(0));
        Assert.That(name, Is.EqualTo("TestConn-Integration"));
    }

    [Test]
    public async Task Get_arr_connection_by_id()
    {
        var id = await CreateTestConnectionAsync();

        var getJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/arrconnections/{id}", _apiKey);
        using var doc = JsonDocument.Parse(getJson);

        var name = doc.RootElement.GetProperty("name").GetString();
        Assert.That(name, Is.EqualTo("TestConn-Integration"));
    }

    [Test]
    public async Task Update_arr_connection_changes_name()
    {
        var id = await CreateTestConnectionAsync();

        var updatePayload = new
        {
            id,
            name = "TestConn-Updated",
            arrType = "Sonarr",
            url = "http://localhost:19999",
            apiKey = "testkey-not-real-00000000",
            implementation = "SonarrConnection",
            configContract = "ArrConnectionDefinition",
            enable = false,
            webhookEnabled = false,
            syncEnabled = false
        };

        var (statusCode, _) = await PutJsonAsync($"{SeedarrUrl}/api/v1/arrconnections/{id}", updatePayload, _apiKey);
        Assert.That(statusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task Delete_arr_connection_removes_it()
    {
        var id = await CreateTestConnectionAsync();

        await DeleteAsync($"{SeedarrUrl}/api/v1/arrconnections/{id}");

        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/arrconnections", _apiKey);
        using var listDoc = JsonDocument.Parse(listJson);

        var ids = listDoc.RootElement.EnumerateArray()
            .Select(c => c.GetProperty("id").GetInt32())
            .ToList();

        Assert.That(ids, Does.Not.Contain(id));
    }

    [Test]
    public async Task Arr_connections_sync_returns_result()
    {
        var responseJson = await PostJsonAsync($"{SeedarrUrl}/api/v1/arrconnections/sync", new { }, _apiKey);
        using var doc = JsonDocument.Parse(responseJson);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }
}
