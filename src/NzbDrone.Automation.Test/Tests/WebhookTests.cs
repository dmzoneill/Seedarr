using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class WebhookTests : ApiTestBase
{
    private const string SonarrHash = "aabbccdd11223344556677889900aabb11223344";
    private const string SonarrDownloadId = "AABBCCDD11223344556677889900AABB11223344";

    private const string RadarrHash = "11223344556677889900aabbccddeeff00112233";
    private const string RadarrDownloadId = "11223344556677889900AABBCCDDEEFF00112233";

    private const string LidarrHash = "ffeeddccbbaa99887766554433221100ffeeddcc";
    private const string LidarrDownloadId = "FFEEDDCCBBAA99887766554433221100FFEEDDCC";

    private const string RealHash = "e63e5567d9352b7b0d7d6d9271c0c5b2a303a059";
    private const string RealDownloadId = "E63E5567D9352B7B0D7D6D9271C0C5B2A303A059";

    private string _apiKey;

    [OneTimeSetUp]
    public async Task SetUpApiKey()
    {
        _apiKey = await GetApiKeyAsync(SeedarrUrl);
    }

    [SetUp]
    public async Task SetUp() => await CleanupTorrentsAsync();

    [TearDown]
    public async Task TearDown() => await CleanupTorrentsAsync();

    private async Task<string> PostWebhookAsync(object body)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await Client.PostAsync($"{SeedarrUrl}/api/v1/webhook/arr", content);
        return await response.Content.ReadAsStringAsync();
    }

    [Test]
    public async Task Download_event_is_ignored()
    {
        var response = await PostWebhookAsync(new { eventType = "Download" });
        using var doc = JsonDocument.Parse(response);
        Assert.That(doc.RootElement.GetProperty("message").GetString(), Is.EqualTo("Ignored event type: Download"));
    }

    [Test]
    public async Task Rename_event_is_ignored()
    {
        var response = await PostWebhookAsync(new { eventType = "Rename" });
        Assert.That(response, Does.Contain("Ignored event type"));
    }

    [Test]
    public async Task Grab_without_downloadId_is_rejected()
    {
        var response = await PostWebhookAsync(new { eventType = "Grab" });
        Assert.That(response, Does.Contain("No downloadId"));
    }

    [Test]
    public async Task Sonarr_grab_creates_torrent()
    {
        var response = await PostWebhookAsync(BuildSonarrGrabPayload(SonarrDownloadId));
        using var doc = JsonDocument.Parse(response);
        Assert.That(doc.RootElement.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(doc.RootElement.GetProperty("infoHash").GetString(), Is.EqualTo(SonarrHash));
    }

    [Test]
    public async Task Sonarr_torrent_appears_in_list()
    {
        await PostWebhookAsync(BuildSonarrGrabPayload(SonarrDownloadId));
        var torrentJson = await FindTorrentByHashAsync(SonarrHash);
        Assert.That(torrentJson, Is.Not.Null, "Torrent not found in list");
        using var doc = JsonDocument.Parse(torrentJson);
        Assert.That(doc.RootElement.GetProperty("name").GetString(), Does.Contain("Integration.Test.Sonarr"));
    }

    [Test]
    public async Task Duplicate_hash_is_rejected()
    {
        var payload = BuildSonarrGrabPayload(SonarrDownloadId);
        await PostWebhookAsync(payload);
        var response = await PostWebhookAsync(payload);
        Assert.That(response, Does.Contain("already exists"));
    }

    [Test]
    public async Task Radarr_grab_creates_torrent()
    {
        var response = await PostWebhookAsync(BuildRadarrGrabPayload(RadarrDownloadId));
        using var doc = JsonDocument.Parse(response);
        Assert.That(doc.RootElement.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(await FindTorrentByHashAsync(RadarrHash), Is.Not.Null, "Radarr torrent not found in list");
    }

    [Test]
    public async Task Lidarr_grab_creates_torrent()
    {
        var response = await PostWebhookAsync(BuildLidarrGrabPayload(LidarrDownloadId));
        using var doc = JsonDocument.Parse(response);
        Assert.That(doc.RootElement.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(await FindTorrentByHashAsync(LidarrHash), Is.Not.Null, "Lidarr torrent not found in list");
    }

    [Test]
    public async Task Webhook_matches_connection_by_application_url()
    {
        var payload = new
        {
            eventType = "Grab",
            applicationUrl = "http://sonarr.local:8989",
            downloadId = "0000000000000000000000000000000000000001",
            downloadClient = "Transmission",
            downloadClientType = "Transmission"
        };

        var response = await PostWebhookAsync(payload);
        Assert.That(response, Does.Contain("basic metadata"));
    }

    [Test]
    public async Task Real_hash_webhook_accepted()
    {
        var response = await PostWebhookAsync(BuildSonarrGrabPayload(RealDownloadId, "VideoHive.Test.1080p.WEB-DL"));
        using var doc = JsonDocument.Parse(response);
        Assert.That(doc.RootElement.GetProperty("success").GetBoolean(), Is.True);
        Assert.That(doc.RootElement.GetProperty("infoHash").GetString(), Is.EqualTo(RealHash));
    }

    [Test]
    public async Task Real_hash_torrent_has_correct_metadata()
    {
        await PostWebhookAsync(BuildSonarrGrabPayload(RealDownloadId, "VideoHive.Test.1080p.WEB-DL"));
        var torrentJson = await FindTorrentByHashAsync(RealHash);
        Assert.That(torrentJson, Is.Not.Null, "Torrent not found in list");
        using var doc = JsonDocument.Parse(torrentJson);
        Assert.That(doc.RootElement.GetProperty("name").GetString(), Does.Contain("VideoHive"));
        Assert.That(doc.RootElement.GetProperty("totalSize").GetInt64(), Is.EqualTo(158649340L));
    }

    [Test]
    public async Task Duplicate_real_hash_is_rejected()
    {
        await PostWebhookAsync(BuildSonarrGrabPayload(RealDownloadId, "VideoHive.Test.1080p.WEB-DL"));
        var response = await PostWebhookAsync(BuildRadarrGrabPayload(RealDownloadId, "VideoHive.Test.Movie.2024.1080p.BluRay"));
        Assert.That(response, Does.Contain("already exists"));
    }

    private static object BuildSonarrGrabPayload(string downloadId, string releaseTitle = "Integration.Test.Sonarr.S01E01.720p.WEB-DL")
    {
        return new
        {
            eventType = "Grab",
            instanceName = "Sonarr",
            applicationUrl = "http://sonarr.local:8989",
            downloadClient = "Transmission",
            downloadClientType = "Transmission",
            downloadId,
            release = new
            {
                releaseTitle,
                indexer = "TestIndexer",
                size = 1073741824L,
                quality = "HDTV-720p",
                releaseGroup = "TestGroup"
            }
        };
    }

    private static object BuildRadarrGrabPayload(string downloadId, string releaseTitle = "Integration.Test.Radarr.2024.1080p.BluRay")
    {
        return new
        {
            eventType = "Grab",
            instanceName = "Radarr",
            applicationUrl = "http://radarr.local:7878",
            downloadClient = "Transmission",
            downloadClientType = "Transmission",
            downloadId,
            release = new
            {
                releaseTitle,
                size = 5368709120L,
                quality = "Bluray-1080p"
            }
        };
    }

    private static object BuildLidarrGrabPayload(string downloadId, string releaseTitle = "Integration.Test.Lidarr.Artist.Album.FLAC")
    {
        return new
        {
            eventType = "Grab",
            instanceName = "Lidarr",
            applicationUrl = "http://lidarr.local:8686",
            downloadClient = "Transmission",
            downloadClientType = "Transmission",
            downloadId,
            release = new
            {
                releaseTitle,
                size = 734003200L,
                quality = "FLAC"
            }
        };
    }

    private async Task<string> FindTorrentByHashAsync(string hash)
    {
        var listJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent", _apiKey);
        using var doc = JsonDocument.Parse(listJson);
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            if (t.TryGetProperty("infoHash", out var h)
                && string.Equals(h.GetString(), hash, StringComparison.OrdinalIgnoreCase))
            {
                return t.GetRawText();
            }
        }

        return null;
    }
}
