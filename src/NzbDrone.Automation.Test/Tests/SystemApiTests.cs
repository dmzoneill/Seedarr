using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class SystemApiTests : ApiTestBase
{
    private string _apiKey;

    [OneTimeSetUp]
    public async Task SetUpApiKey()
    {
        _apiKey = await GetApiKeyAsync(SeedarrUrl);
    }

    [Test]
    public async Task System_tasks_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/system/task", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task System_tasks_have_required_fields()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/system/task", _apiKey);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetArrayLength() == 0)
            return;

        foreach (var task in doc.RootElement.EnumerateArray())
        {
            Assert.That(task.TryGetProperty("typeName", out var typeName), Is.True, "Task missing typeName property");
            Assert.That(typeName.GetString(), Is.Not.Null.And.Not.Empty, "typeName must be a non-empty string");
            Assert.That(task.TryGetProperty("interval", out var interval), Is.True, "Task missing interval property");
            Assert.That(interval.GetInt32(), Is.GreaterThanOrEqualTo(0), "interval must be >= 0");
        }
    }

    [Test]
    public async Task System_command_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/system/command", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task System_status_returns_app_name()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/system/status", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        var hasAppName = doc.RootElement.TryGetProperty("appName", out _);
        var hasVersion = doc.RootElement.TryGetProperty("version", out _);
        Assert.That(hasAppName || hasVersion, Is.True, "system/status must have appName or version property");
    }
}
