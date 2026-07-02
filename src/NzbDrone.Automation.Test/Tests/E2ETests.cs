using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class E2ETests : ApiTestBase
{
    private const string TestTorrentHash = "e63e5567d9352b7b0d7d6d9271c0c5b2a303a059";

    private string _radarrKey = string.Empty;

    [OneTimeSetUp]
    public async Task SetupE2E()
    {
        _radarrKey = await GetApiKeyAsync(RadarrUrl);
        await CleanupE2eAsync(_radarrKey);
    }

    [OneTimeTearDown]
    public async Task TeardownE2E()
    {
        await CleanupE2eAsync(_radarrKey);
    }

    [Test]
    [CancelAfter(120000)]
    public async Task Full_E2E_radarr_release_triggers_transmission_and_seedarr_enrichment()
    {
        await CleanupE2eAsync(_radarrKey);

        if (string.IsNullOrEmpty(_radarrKey))
            Assert.Ignore("Radarr not available — no API key");

        await Task.Delay(TimeSpan.FromSeconds(2));

        var moviesJson = await GetJsonAsync($"{RadarrUrl}/api/v3/movie", _radarrKey);
        using var moviesDoc = JsonDocument.Parse(moviesJson);
        var matrixExists = false;
        foreach (var movie in moviesDoc.RootElement.EnumerateArray())
        {
            if (movie.TryGetProperty("tmdbId", out var tmdbEl) && tmdbEl.GetInt32() == 603)
            {
                matrixExists = true;
                break;
            }
        }

        if (!matrixExists)
            Assert.Ignore("The Matrix (tmdbId=603) not in Radarr library");

        var torrentUrl = $"{SeedarrUrl}/fixtures/test.torrent";
        var pushBody = new
        {
            title = "The.Matrix.1999.1080p.BluRay.x264-TestGroup",
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

        Assert.That(approved, Is.True, "Radarr release/push accepted");

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
                continue;
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

        Assert.That(inQueue, Is.True, "Torrent in Radarr queue");

        await Task.Delay(TimeSpan.FromSeconds(20));

        var transJson = await TransmissionRpcAsync("torrent-get", new
        {
            ids = new[] { TestTorrentHash },
            fields = new[] { "hashString", "name", "totalSize" }
        });

        using var transDoc = JsonDocument.Parse(transJson);
        var transTorrents = transDoc.RootElement
            .GetProperty("arguments")
            .GetProperty("torrents");

        var transHash = string.Empty;
        var transSize = 0L;
        foreach (var t in transTorrents.EnumerateArray())
        {
            if (t.TryGetProperty("hashString", out var hashEl))
                transHash = hashEl.GetString() ?? string.Empty;
            if (t.TryGetProperty("totalSize", out var sizeEl))
                transSize = sizeEl.GetInt64();
        }

        Assert.That(transHash, Is.EqualTo(TestTorrentHash).IgnoreCase, "Torrent in Transmission");
        Assert.That(transSize, Is.EqualTo(158649340L), "Transmission size matches");

        var seedarrKey = await GetApiKeyAsync(SeedarrUrl);
        var seedarrJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent", seedarrKey);
        using var seedarrDoc = JsonDocument.Parse(seedarrJson);

        var foundInSeedarr = false;
        var pieceCount = 0;
        var pieceLength = 0;

        foreach (var torrent in seedarrDoc.RootElement.EnumerateArray())
        {
            var hashStr = torrent.TryGetProperty("infoHash", out var hashEl2)
                ? hashEl2.GetString() ?? string.Empty
                : string.Empty;

            if (!hashStr.Equals(TestTorrentHash, StringComparison.OrdinalIgnoreCase))
                continue;

            foundInSeedarr = true;
            if (torrent.TryGetProperty("pieceCount", out var pcEl))
                pieceCount = pcEl.GetInt32();
            if (torrent.TryGetProperty("pieceLength", out var plEl))
                pieceLength = plEl.GetInt32();
            break;
        }

        Assert.That(foundInSeedarr, Is.True, "Torrent in Seedarr");
        Assert.That(pieceCount, Is.EqualTo(1211), "Seedarr pieceCount from .torrent");
        Assert.That(pieceLength, Is.EqualTo(131072), "Seedarr pieceLength from .torrent");
    }

    private async Task CleanupE2eAsync(string radarrKey)
    {
        await CleanupTorrentsAsync();

        try
        {
            await TransmissionRpcAsync("torrent-remove", new
            {
                ids = new[] { TestTorrentHash },
                deleteLocalData = true
            });
        }
        catch
        {
        }

        if (string.IsNullOrEmpty(radarrKey))
            return;

        try
        {
            var queueJson = await GetJsonAsync($"{RadarrUrl}/api/v3/queue", radarrKey);
            using var queueDoc = JsonDocument.Parse(queueJson);
            var records = queueDoc.RootElement.TryGetProperty("records", out var recordsEl)
                ? recordsEl
                : queueDoc.RootElement;

            if (records.ValueKind == JsonValueKind.Array)
            {
                foreach (var record in records.EnumerateArray())
                {
                    if (!record.TryGetProperty("downloadId", out var dlId))
                        continue;
                    var dlIdStr = dlId.GetString() ?? string.Empty;
                    if (!dlIdStr.Contains("E63E5567", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!record.TryGetProperty("id", out var idEl))
                        continue;
                    var id = idEl.GetInt32();
                    await DeleteWithKeyAsync(
                        $"{RadarrUrl}/api/v3/queue/{id}?removeFromClient=true&blocklist=false",
                        radarrKey);
                }
            }
        }
        catch
        {
        }

        try
        {
            var moviesJson = await GetJsonAsync($"{RadarrUrl}/api/v3/movie", radarrKey);
            using var moviesDoc = JsonDocument.Parse(moviesJson);

            var movieId = -1;
            foreach (var movie in moviesDoc.RootElement.EnumerateArray())
            {
                if (movie.TryGetProperty("tmdbId", out var tmdbEl) && tmdbEl.GetInt32() == 603)
                {
                    movieId = movie.GetProperty("id").GetInt32();
                    break;
                }
            }

            if (movieId != -1)
            {
                await DeleteWithKeyAsync(
                    $"{RadarrUrl}/api/v3/movie/{movieId}?deleteFiles=true",
                    radarrKey);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            var lookupJson = await GetJsonAsync(
                $"{RadarrUrl}/api/v3/movie/lookup/tmdb?tmdbId=603",
                radarrKey);

            var lookupNode = JsonNode.Parse(lookupJson);
            if (lookupNode is JsonObject movieObj)
            {
                movieObj["rootFolderPath"] = "/config/movies";
                movieObj["qualityProfileId"] = 1;
                movieObj["monitored"] = true;
                movieObj["addOptions"] = new JsonObject { ["searchForMovie"] = false };

                var addJson = movieObj.ToJsonString();
                using var addRequest = new HttpRequestMessage(HttpMethod.Post, $"{RadarrUrl}/api/v3/movie")
                {
                    Content = new StringContent(addJson, Encoding.UTF8, "application/json")
                };
                addRequest.Headers.Add("X-Api-Key", radarrKey);
                await Client.SendAsync(addRequest);
            }
        }
        catch
        {
        }
    }

    private async Task DeleteWithKeyAsync(string url, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Add("X-Api-Key", apiKey);
        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
