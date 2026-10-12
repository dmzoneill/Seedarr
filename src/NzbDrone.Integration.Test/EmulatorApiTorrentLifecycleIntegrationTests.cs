using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class EmulatorApiTorrentLifecycleIntegrationTests : IntegrationTestBase
{
    private int _torrentId;
    private string _infoHash = string.Empty;

    [SetUp]
    public async Task UploadFixtureTorrent()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "test.torrent");
        Assume.That(File.Exists(fixturePath), "test.torrent fixture not found");

        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(fixturePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
        content.Add(fileContent, "file", "emulator-lifecycle.torrent");

        var uploadResponse = await Client.PostAsync("/api/v1/torrent/upload", content);
        Assume.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadJson);
        _torrentId = uploadDoc.RootElement.GetProperty("added")[0].GetProperty("id").GetInt32();

        var getResponse = await GetAsync($"/api/v1/torrent/{_torrentId}");
        getResponse.EnsureSuccessStatusCode();
        var torrentJson = await getResponse.Content.ReadAsStringAsync();
        using var torrentDoc = JsonDocument.Parse(torrentJson);
        _infoHash = torrentDoc.RootElement.GetProperty("infoHash").GetString()!.ToLowerInvariant();
    }

    [TearDown]
    public async Task CleanupTorrent()
    {
        if (_torrentId > 0)
        {
            await DeleteAsync($"/api/v1/torrent/{_torrentId}");
            _torrentId = 0;
        }
    }

    [Test]
    public async Task QBittorrent_scoped_gets_and_form_mutations_return_success()
    {
        var hash = _infoHash;
        var scopedGets = new[]
        {
            $"/api/v2/torrents/properties?hash={hash}",
            $"/api/v2/torrents/files?hash={hash}",
            $"/api/v2/torrents/trackers?hash={hash}",
            $"/api/v2/torrents/pieceStates?hash={hash}",
            $"/api/v2/sync/torrentPeers?hash={hash}",
        };

        foreach (var path in scopedGets)
        {
            var response = await Client.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
        }

        await PostFormOkAsync("/api/v2/torrents/pause", ("hashes", hash));
        await PostFormOkAsync("/api/v2/torrents/resume", ("hashes", hash));
        await PostFormOkAsync("/api/v2/torrents/setCategory", ("hashes", hash), ("category", "sonar-emulator"));
        await PostFormOkAsync("/api/v2/torrents/createTags", ("tags", "sonar-emulator-tag"));
        await PostFormOkAsync("/api/v2/torrents/addTags", ("hashes", hash), ("tags", "sonar-emulator-tag"));
        await PostFormOkAsync("/api/v2/torrents/setDownloadLimit", ("hashes", hash), ("limit", "512"));
        await PostFormOkAsync("/api/v2/torrents/setUploadLimit", ("hashes", hash), ("limit", "1024"));
        await PostFormOkAsync("/api/v2/torrents/toggleSequentialDownload", ("hashes", hash));
        await PostFormOkAsync("/api/v2/torrents/setForceStart", ("hashes", hash), ("value", "true"));
        await PostFormOkAsync("/api/v2/torrents/reannounce", ("hashes", hash));
        await PostFormOkAsync("/api/v2/torrents/recheck", ("hashes", hash));
        await PostFormOkAsync("/api/v2/transfer/toggleSpeedLimitsMode");
        await PostFormOkAsync("/api/v2/transfer/setDownloadLimit", ("limit", "2048"));
        await PostFormOkAsync("/api/v2/transfer/setUploadLimit", ("limit", "4096"));
    }

    [Test]
    public async Task Deluge_torrent_scoped_methods_return_results()
    {
        var hash = _infoHash;
        var methods = new (string method, object[] @params)[]
        {
            ("core.get_torrent_status", new object[] { hash, new[] { "name", "state", "progress", "total_size" } }),
            ("core.get_torrents_status", new object[] { new Dictionary<string, object>(), new[] { "name", "state" } }),
            ("core.pause_torrent", new object[] { hash }),
            ("core.resume_torrent", new object[] { hash }),
            ("core.force_recheck", new object[] { new[] { hash } }),
            ("core.force_reannounce", new object[] { new[] { hash } }),
            ("core.set_torrent_options", new object[] { hash, new Dictionary<string, object> { ["sequential_download"] = true } }),
        };

        foreach (var (method, parameters) in methods)
        {
            var payload = new { method, @params = parameters, id = 7 };
            var response = await PostDelugeAsync(payload);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), method);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.That(doc.RootElement.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null), method);
        }
    }

    [Test]
    public async Task Transmission_torrent_actions_succeed_with_session()
    {
        var sessionId = await GetTransmissionSessionIdAsync();
        var id = _torrentId;

        var torrentGet = await PostTransmissionAsync(sessionId, new
        {
            method = "torrent-get",
            arguments = new { ids = new[] { id }, fields = new[] { "id", "name", "status", "hashString" } },
            tag = 11,
        });
        Assert.That(torrentGet.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var torrentStop = await PostTransmissionAsync(sessionId, new
        {
            method = "torrent-stop",
            arguments = new { ids = new[] { id } },
            tag = 12,
        });
        Assert.That(torrentStop.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var torrentStart = await PostTransmissionAsync(sessionId, new
        {
            method = "torrent-start",
            arguments = new { ids = new[] { id } },
            tag = 13,
        });
        Assert.That(torrentStart.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var verify = await PostTransmissionAsync(sessionId, new
        {
            method = "torrent-verify",
            arguments = new { ids = new[] { id } },
            tag = 14,
        });
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var reannounce = await PostTransmissionAsync(sessionId, new
        {
            method = "torrent-reannounce",
            arguments = new { ids = new[] { id } },
            tag = 15,
        });
        Assert.That(reannounce.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task PostFormOkAsync(string path, params (string key, string value)[] fields)
    {
        var form = new List<KeyValuePair<string, string>>();
        foreach (var (key, value) in fields)
        {
            form.Add(new(key, value));
        }

        var response = await Client.PostAsync(path, new FormUrlEncodedContent(form));
        Assert.That(response.IsSuccessStatusCode, Is.True, $"{path} returned {response.StatusCode}");
    }

    private async Task<HttpResponseMessage> PostDelugeAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await Client.PostAsync("/json", content);
    }

    private async Task<string> GetTransmissionSessionIdAsync()
    {
        var payload = new { method = "session-get", tag = 99 };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/transmission/rpc", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(response.Headers.Contains("X-Transmission-Session-Id"), Is.True);
        return string.Join(string.Empty, response.Headers.GetValues("X-Transmission-Session-Id"));
    }

    private async Task<HttpResponseMessage> PostTransmissionAsync(string sessionId, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/transmission/rpc")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);
        return await Client.SendAsync(request);
    }
}
