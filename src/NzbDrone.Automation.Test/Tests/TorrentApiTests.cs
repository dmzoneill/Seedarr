using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class TorrentApiTests : ApiTestBase
{
    [SetUp]
    public async Task CleanUp()
    {
        await CleanupTorrentsAsync();
    }

    [TearDown]
    public async Task CleanUpAfter()
    {
        await CleanupTorrentsAsync();
    }

    [Test]
    public async Task Upload_torrent_file_returns_info_hash()
    {
        using var doc = await UploadTestTorrentAsync();
        Assert.That(doc, Is.Not.Null, "Could not find test.torrent fixture file");
        var infoHash = doc.RootElement.GetProperty("infoHash").GetString();
        Assert.That(infoHash, Is.EqualTo("e63e5567d9352b7b0d7d6d9271c0c5b2a303a059"));
    }

    [Test]
    public async Task Upload_torrent_file_parses_name()
    {
        using var doc = await UploadTestTorrentAsync();
        Assert.That(doc, Is.Not.Null, "Could not find test.torrent fixture file");
        var name = doc.RootElement.GetProperty("name").GetString();
        Assert.That(name, Does.Contain("VideoHive"));
    }

    [Test]
    public async Task Uploaded_torrent_can_be_deleted()
    {
        using var doc = await UploadTestTorrentAsync();
        Assert.That(doc, Is.Not.Null, "Could not find test.torrent fixture file");
        var id = doc.RootElement.GetProperty("id").GetInt32();

        var deleted = await DeleteAsync($"{SeedarrUrl}/api/v1/torrent/{id}");
        Assert.That(deleted, Is.True);

        var apiKey = await GetApiKeyAsync(SeedarrUrl);
        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent", apiKey);
        using var listDoc = JsonDocument.Parse(listJson);

        var found = false;
        foreach (var torrent in listDoc.RootElement.EnumerateArray())
        {
            if (torrent.TryGetProperty("id", out var idProp) && idProp.GetInt32() == id)
            {
                found = true;
                break;
            }
        }

        Assert.That(found, Is.False, $"Torrent with id {id} should have been deleted");
    }
}
