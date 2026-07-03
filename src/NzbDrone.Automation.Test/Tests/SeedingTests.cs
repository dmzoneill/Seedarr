using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class SeedingTests : ApiTestBase
{
    private int _torrentId;

    [OneTimeSetUp]
    public async Task SetUpTorrent()
    {
        await CleanupTorrentsAsync();

        using var doc = await UploadTestTorrentAsync();
        if (doc != null && doc.RootElement.TryGetProperty("id", out var idProp))
            _torrentId = idProp.GetInt32();
    }

    [OneTimeTearDown]
    public async Task TearDownTorrent()
    {
        if (_torrentId > 0)
            await DeleteAsync($"{SeedarrUrl}/api/v1/torrent/{_torrentId}");
    }

    [Test]
    public async Task Seeding_stats_returns_object()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/seeding/stats");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task Seeding_stats_has_expected_fields()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/seeding/stats");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var hasExpectedField =
            root.TryGetProperty("activeTorrents", out _) ||
            root.TryGetProperty("totalUploaded", out _) ||
            root.TryGetProperty("uploadSpeed", out _) ||
            root.TryGetProperty("totalTorrents", out _);

        Assert.That(hasExpectedField, Is.True, "Stats object should contain at least one of: activeTorrents, totalUploaded, uploadSpeed, totalTorrents");
    }

    [Test]
    public async Task Seeding_history_returns_array()
    {
        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/seeding/history");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Seeding_torrent_history_returns_array()
    {
        if (_torrentId <= 0)
            Assert.Ignore("No torrent was uploaded; skipping per-torrent history test.");

        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/seeding/history/{_torrentId}");
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Start_all_returns_ok()
    {
        var response = await Client.PostAsync($"{SeedarrUrl}/api/v1/seeding/start-all", null);
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task Stop_all_returns_ok()
    {
        var response = await Client.PostAsync($"{SeedarrUrl}/api/v1/seeding/stop-all", null);
        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task Start_torrent_returns_ok()
    {
        if (_torrentId <= 0)
            Assert.Ignore("No torrent was uploaded; skipping per-torrent start test.");

        var response = await Client.PostAsync($"{SeedarrUrl}/api/v1/seeding/start/{_torrentId}", null);
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task Stop_torrent_returns_ok()
    {
        if (_torrentId <= 0)
            Assert.Ignore("No torrent was uploaded; skipping per-torrent stop test.");

        var response = await Client.PostAsync($"{SeedarrUrl}/api/v1/seeding/stop/{_torrentId}", null);
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }
}
