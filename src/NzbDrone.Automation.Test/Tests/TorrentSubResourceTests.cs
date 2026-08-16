using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests;

[TestFixture]
public class TorrentSubResourceTests : ApiTestBase
{
    private int _torrentId;
    private string _infoHash;
    private string _apiKey;

    [OneTimeSetUp]
    public async Task SetUpTorrent()
    {
        await CleanupTorrentsAsync();
        _apiKey = await GetApiKeyAsync(SeedarrUrl);

        using var doc = await UploadTestTorrentAsync();
        if (doc == null)
            return;

        _torrentId = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
        _infoHash = doc.RootElement.TryGetProperty("infoHash", out var hashProp) ? hashProp.GetString() : string.Empty;
    }

    [OneTimeTearDown]
    public async Task TearDownTorrent()
    {
        if (_torrentId > 0)
            await DeleteAsync($"{SeedarrUrl}/api/v1/torrent/{_torrentId}");
    }

    private void SkipIfNoTorrent()
    {
        if (_torrentId <= 0)
            Assert.Ignore("Torrent upload failed or fixture not found; skipping sub-resource tests.");
    }

    private async Task<HttpResponseMessage> PostTorrentActionAsync(string action)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SeedarrUrl}/api/v1/torrent/{_torrentId}/{action}");
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);
        return await Client.SendAsync(request);
    }

    [Test]
    public async Task Get_torrent_by_id_returns_torrent()
    {
        SkipIfNoTorrent();

        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent/{_torrentId}", _apiKey);
        using var doc = JsonDocument.Parse(json);
        var infoHash = doc.RootElement.GetProperty("infoHash").GetString();
        Assert.That(infoHash, Is.EqualTo(_infoHash));
    }

    [Test]
    public async Task Get_torrent_by_id_has_correct_name()
    {
        SkipIfNoTorrent();

        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent/{_torrentId}", _apiKey);
        using var doc = JsonDocument.Parse(json);
        var name = doc.RootElement.GetProperty("name").GetString();
        Assert.That(name, Does.Contain("VideoHive"));
    }

    [Test]
    public async Task Get_torrent_by_id_has_correct_size()
    {
        SkipIfNoTorrent();

        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent/{_torrentId}", _apiKey);
        using var doc = JsonDocument.Parse(json);
        var totalSize = doc.RootElement.GetProperty("totalSize").GetInt64();
        Assert.That(totalSize, Is.EqualTo(158649340L));
    }

    [Test]
    public async Task Get_torrent_files_returns_array()
    {
        SkipIfNoTorrent();

        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent/{_torrentId}/files", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Get_torrent_trackers_returns_array()
    {
        SkipIfNoTorrent();

        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent/{_torrentId}/trackers", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Get_torrent_peers_returns_array()
    {
        SkipIfNoTorrent();

        var json = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent/{_torrentId}/peers", _apiKey);
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
    }

    [Test]
    public async Task Update_torrent_label_persists()
    {
        SkipIfNoTorrent();

        var rawJson = await GetJsonAsync($"{SeedarrUrl}/api/v1/torrent/{_torrentId}", _apiKey);
        var node = JsonNode.Parse(rawJson);
        node["label"] = "test-label";
        var updatedJson = node.ToJsonString();

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, $"{SeedarrUrl}/api/v1/torrent/{_torrentId}")
        {
            Content = new StringContent(updatedJson, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(_apiKey))
            putRequest.Headers.Add("X-Api-Key", _apiKey);
        var putResponse = await Client.SendAsync(putRequest);
        var responseBody = await putResponse.Content.ReadAsStringAsync();
        Assert.That(responseBody, Does.Contain("test-label"));
    }

    [Test]
    public async Task Announce_torrent_returns_ok()
    {
        SkipIfNoTorrent();
        var response = await PostTorrentActionAsync("announce");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Recheck_torrent_returns_ok()
    {
        SkipIfNoTorrent();
        var response = await PostTorrentActionAsync("recheck");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
