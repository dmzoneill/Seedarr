using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class UpdateApiTests : ApiTestBase
{
    [Test]
    public async Task Update_endpoint_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/update");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Update_list_contains_installed_version()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/update");
        using var doc = JsonDocument.Parse(json);

        var hasInstalled = false;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("installed", out var installed) && installed.GetBoolean())
            {
                hasInstalled = true;
                break;
            }
        }

        Assert.That(hasInstalled, Is.True, "Update list must contain at least one entry with installed=true");
    }

    [Test]
    public async Task Update_entries_have_required_fields()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/update");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetArrayLength() == 0)
            Assert.Ignore("No update entries returned; skipping field check.");

        var first = doc.RootElement[0];
        Assert.That(first.TryGetProperty("version", out var version), Is.True, "Update entry missing 'version'");
        Assert.That(version.GetString(), Is.Not.Null.And.Not.Empty, "'version' must be non-empty");
        Assert.That(first.TryGetProperty("installed", out _), Is.True, "Update entry missing 'installed'");
        Assert.That(first.TryGetProperty("latest", out _), Is.True, "Update entry missing 'latest'");
        Assert.That(first.TryGetProperty("changes", out _), Is.True, "Update entry missing 'changes'");
    }
}
