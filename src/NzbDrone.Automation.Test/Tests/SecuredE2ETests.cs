using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

/// <summary>
/// E2E tests for the authenticated webhook flow. Webhook endpoint requires
/// X-Api-Key like all other Seedarr API endpoints. Arr apps include X-Api-Key
/// in webhook notifications, registered by Seedarr during connection setup.
/// </summary>
[TestFixture]
public class WebhookAuthE2ETests : ApiTestBase
{
    private const int TestMovieTmdbId = 27205;
    private const string TestReleaseTitle = "Inception.2010.1080p.BluRay.x264-TestGroup";
    private const string TestTorrentHash = "e63e5567d9352b7b0d7d6d9271c0c5b2a303a059";

    private string _seedarrKey = string.Empty;
    private string _radarrKey = string.Empty;

    [OneTimeSetUp]
    public async Task Setup()
    {
        _seedarrKey = await GetApiKeyAsync(SeedarrUrl);
        _radarrKey = await GetApiKeyAsync(RadarrUrl);

        if (string.IsNullOrEmpty(_radarrKey))
        {
            Assert.Ignore("Radarr not available");
            return;
        }

        await CleanupAsync();
        await EnsureMovieInRadarrAsync(TestMovieTmdbId);
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        await CleanupAsync();
    }

    [Test]
    public async Task Webhook_without_api_key_is_rejected()
    {
        var (status, _) = await SendWebhookDirectAsync(
            new { eventType = "Test", instanceName = "Radarr" },
            null);

        Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized), "Webhook without X-Api-Key must be rejected");
    }

    [Test]
    public async Task Webhook_with_wrong_api_key_is_rejected()
    {
        var (status, _) = await SendWebhookDirectAsync(
            new { eventType = "Test", instanceName = "Radarr" },
            "wrong-key-that-does-not-match");

        Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized), "Webhook with wrong X-Api-Key must be rejected");
    }

    [Test]
    public async Task Webhook_with_correct_api_key_is_accepted()
    {
        var (status, body) = await SendWebhookDirectAsync(
            new
            {
                eventType = "Grab",
                instanceName = "Radarr",
                downloadId = "aabbccdd11223344aabbccdd11223344aabbccdd",
                release = new { releaseTitle = "Test.Movie.2024", size = 1073741824L }
            },
            _seedarrKey);

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK), "Webhook with correct X-Api-Key must be accepted");
        Assert.That(body, Does.Contain("success"), "Response should be a success result");

        await CleanupTorrentByHashAsync("aabbccdd11223344aabbccdd11223344aabbccdd");
    }

    [Test]
    [CancelAfter(120000)]
    public async Task Full_E2E_radarr_grab_triggers_authenticated_webhook_to_seedarr()
    {
        await CleanupAsync();

        if (string.IsNullOrEmpty(_radarrKey))
        {
            Assert.Ignore("Radarr not available");
        }

        var seedarrInternalUrl = Environment.GetEnvironmentVariable("SEEDARR_INTERNAL_URL") ?? "http://seedarr.local:9898";
        var torrentUrl = $"{seedarrInternalUrl}/fixtures/test.torrent";

        var moviesJson = await GetJsonAsync($"{RadarrUrl}/api/v3/movie", _radarrKey);
        using var moviesDoc = JsonDocument.Parse(moviesJson);
        var movieExists = false;
        foreach (var movie in moviesDoc.RootElement.EnumerateArray())
        {
            if (movie.TryGetProperty("tmdbId", out var tmdbEl) && tmdbEl.GetInt32() == TestMovieTmdbId)
            {
                movieExists = true;
                break;
            }
        }

        if (!movieExists)
        {
            Assert.Ignore($"Inception (tmdbId={TestMovieTmdbId}) not in Radarr library — setup may have failed");
        }

        var pushBody = new
        {
            title = TestReleaseTitle,
            downloadUrl = torrentUrl,
            protocol = "torrent",
            publishDate = "2024-01-01T00:00:00Z",
            size = 158649340,
            indexer = "TestFixture"
        };

        var pushJson = await PostJsonAsync($"{RadarrUrl}/api/v3/release/push", pushBody, _radarrKey);

        var approved = false;
        using var pushDoc = JsonDocument.Parse(pushJson);
        if (pushDoc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pushDoc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("approved", out var approvedEl) && approvedEl.GetBoolean())
                {
                    approved = true;
                    break;
                }
            }
        }
        else if (pushDoc.RootElement.ValueKind == JsonValueKind.Object
            && pushDoc.RootElement.TryGetProperty("approved", out var approvedEl2))
        {
            approved = approvedEl2.GetBoolean();
        }

        Assert.That(approved, Is.True, "Radarr release/push accepted for Inception");

        var inQueue = false;
        for (var poll = 0; poll < 20 && !inQueue; poll++)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var queueJson = await GetJsonAsync($"{RadarrUrl}/api/v3/queue", _radarrKey);
            using var queueDoc = JsonDocument.Parse(queueJson);
            var queueRecords = queueDoc.RootElement.TryGetProperty("records", out var recordsEl)
                ? recordsEl
                : queueDoc.RootElement;
            if (queueRecords.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var record in queueRecords.EnumerateArray())
            {
                if (record.TryGetProperty("downloadId", out var dlId)
                    && (dlId.GetString() ?? string.Empty).Contains("E63E5567", StringComparison.OrdinalIgnoreCase))
                {
                    inQueue = true;
                    break;
                }
            }
        }

        Assert.That(inQueue, Is.True, "Torrent in Radarr queue after Inception push");

        await Task.Delay(TimeSpan.FromSeconds(20));

        var seedarrJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent");
        using var seedarrDoc = JsonDocument.Parse(seedarrJson);

        var foundInSeedarr = false;
        foreach (var torrent in seedarrDoc.RootElement.EnumerateArray())
        {
            var hashStr = torrent.TryGetProperty("infoHash", out var hashEl)
                ? hashEl.GetString() ?? string.Empty
                : string.Empty;
            if (hashStr.Equals(TestTorrentHash, StringComparison.OrdinalIgnoreCase))
            {
                foundInSeedarr = true;
                break;
            }
        }

        Assert.That(foundInSeedarr, Is.True, "Torrent in Seedarr via authenticated webhook — X-Api-Key accepted");
    }

    private async Task<(HttpStatusCode Status, string Body)> SendWebhookDirectAsync(object payload, string apiKey)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SeedarrUrl}/api/v1/webhook/arr")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, body);
    }

    private async Task EnsureMovieInRadarrAsync(int tmdbId)
    {
        try
        {
            var moviesJson = await GetJsonAsync($"{RadarrUrl}/api/v3/movie", _radarrKey);
            using var moviesDoc = JsonDocument.Parse(moviesJson);
            foreach (var movie in moviesDoc.RootElement.EnumerateArray())
            {
                if (movie.TryGetProperty("tmdbId", out var tmdbEl) && tmdbEl.GetInt32() == tmdbId)
                {
                    return;
                }
            }

            var lookupJson = await GetJsonAsync($"{RadarrUrl}/api/v3/movie/lookup/tmdb?tmdbId={tmdbId}", _radarrKey);
            var lookupNode = JsonNode.Parse(lookupJson);
            if (lookupNode is JsonObject movieObj)
            {
                movieObj["rootFolderPath"] = "/config/movies";
                movieObj["qualityProfileId"] = 1;
                movieObj["monitored"] = true;
                movieObj["addOptions"] = new JsonObject { ["searchForMovie"] = false };
                await PostJsonAsync($"{RadarrUrl}/api/v3/movie", movieObj, _radarrKey);
            }
        }
        catch { }
    }

    private async Task CleanupTorrentByHashAsync(string infoHash)
    {
        try
        {
            var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent");
            using var doc = JsonDocument.Parse(json);
            foreach (var torrent in doc.RootElement.EnumerateArray())
            {
                var hashStr = torrent.TryGetProperty("infoHash", out var h) ? h.GetString() ?? "" : "";
                if (!hashStr.Equals(infoHash, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = torrent.GetProperty("id").GetInt32();
                await DeleteAsync($"{SeedarrUrl}/api/v1/torrent/{id}");
            }
        }
        catch { }
    }

    private async Task CleanupAsync()
    {
        try
        {
            var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent");
            using var doc = JsonDocument.Parse(json);
            foreach (var torrent in doc.RootElement.EnumerateArray())
            {
                var hashStr = torrent.TryGetProperty("infoHash", out var h) ? h.GetString() ?? "" : "";
                if (!hashStr.Equals(TestTorrentHash, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = torrent.GetProperty("id").GetInt32();
                await DeleteAsync($"{SeedarrUrl}/api/v1/torrent/{id}");
            }
        }
        catch { }

        try
        {
            await TransmissionRpcAsync("torrent-remove", new { ids = new[] { TestTorrentHash }, deleteLocalData = true });
        }
        catch { }

        if (string.IsNullOrEmpty(_radarrKey))
        {
            return;
        }

        try
        {
            var queueJson = await GetJsonAsync($"{RadarrUrl}/api/v3/queue", _radarrKey);
            using var queueDoc = JsonDocument.Parse(queueJson);
            var records = queueDoc.RootElement.TryGetProperty("records", out var recordsEl)
                ? recordsEl
                : queueDoc.RootElement;

            if (records.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var record in records.EnumerateArray())
            {
                if (!record.TryGetProperty("downloadId", out var dlId))
                {
                    continue;
                }

                if (!(dlId.GetString() ?? string.Empty).Contains("E63E5567", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!record.TryGetProperty("id", out var queueId))
                {
                    continue;
                }

                await DeleteWithKeyAsync(
                    $"{RadarrUrl}/api/v3/queue/{queueId.GetInt32()}?removeFromClient=true&blocklist=false",
                    _radarrKey);
            }
        }
        catch { }
    }

    private async Task DeleteWithKeyAsync(string url, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        await Client.SendAsync(request);
    }
}
