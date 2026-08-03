using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class ArrSyncTests : ApiTestBase
{
    [Test]
    public async Task Sync_returns_ok()
    {
        var json = await PostJsonAsync($"{SeedarrUrl}/api/v1/arrsync/sync", new { });
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task Sync_result_has_added_skipped_failed()
    {
        var json = await PostJsonAsync($"{SeedarrUrl}/api/v1/arrsync/sync", new { });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.That(root.TryGetProperty("added", out var added), Is.True, "Sync result missing 'added'");
        Assert.That(root.TryGetProperty("skipped", out var skipped), Is.True, "Sync result missing 'skipped'");
        Assert.That(root.TryGetProperty("failed", out var failed), Is.True, "Sync result missing 'failed'");

        Assert.That(added.GetInt32(), Is.GreaterThanOrEqualTo(0));
        Assert.That(skipped.GetInt32(), Is.GreaterThanOrEqualTo(0));
        Assert.That(failed.GetInt32(), Is.GreaterThanOrEqualTo(0));
    }
}
