using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class TorrentControllerTests : IntegrationTestBase
{
    [Test]
    public async Task GetTorrents_returns_200_and_array()
    {
        var response = await GetAsync("/api/v1/torrent");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var list = Deserialize<List<Dictionary<string, object>>>(json);

        Assert.That(list, Is.Not.Null);
    }

    [Test]
    public async Task UploadTorrent_returns_added_result()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "test.torrent");
        Assume.That(File.Exists(fixturePath), "test.torrent fixture not found");

        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(fixturePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
        content.Add(fileContent, "file", "test.torrent");

        var response = await Client.PostAsync("/api/v1/torrent/upload", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        using var doc = Deserialize<JsonDocument>(json);

        Assert.That(doc, Is.Not.Null);
        Assert.That(doc.RootElement.GetProperty("added").GetArrayLength(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task UploadTorrent_multiple_files_reports_duplicates_as_failed()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "test.torrent");
        Assume.That(File.Exists(fixturePath), "test.torrent fixture not found");

        var fileBytes = await File.ReadAllBytesAsync(fixturePath);
        using var content = new MultipartFormDataContent();
        for (var i = 0; i < 2; i++)
        {
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            content.Add(fileContent, "file", $"test-{i}.torrent");
        }

        var response = await Client.PostAsync("/api/v1/torrent/upload", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        using var doc = Deserialize<JsonDocument>(json);

        Assert.That(doc.RootElement.GetProperty("added").GetArrayLength(), Is.EqualTo(1));
        Assert.That(doc.RootElement.GetProperty("failed").GetArrayLength(), Is.EqualTo(1));
        Assert.That(
            doc.RootElement.GetProperty("failed")[0].GetProperty("reason").GetString(),
            Does.Contain("already exists"));
    }

    [Test]
    public async Task GetTorrentLogs_after_upload_returns_added_event()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "test.torrent");
        Assume.That(File.Exists(fixturePath), "test.torrent fixture not found");

        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(fixturePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
        content.Add(fileContent, "file", "logtest.torrent");

        var uploadResponse = await Client.PostAsync("/api/v1/torrent/upload", content);
        Assume.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = Deserialize<JsonDocument>(uploadJson);
        Assert.That(uploadDoc.RootElement.GetProperty("added").GetArrayLength(), Is.EqualTo(1));
        var torrentId = uploadDoc.RootElement.GetProperty("added")[0].GetProperty("id").GetInt32();

        var logsResponse = await GetAsync($"/api/v1/torrent/{torrentId}/logs");

        Assert.That(logsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logsJson = await logsResponse.Content.ReadAsStringAsync();
        using var logsDoc = Deserialize<JsonDocument>(logsJson);

        Assert.That(logsDoc.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(1));

        var first = logsDoc.RootElement[0];
        Assert.That(first.GetProperty("torrentId").GetInt32(), Is.EqualTo(torrentId));
        Assert.That(first.GetProperty("message").GetString(), Does.Contain("added from file"));
    }

    [Test]
    public async Task GetTorrentById_unknown_returns_404()
    {
        var response = await GetAsync("/api/v1/torrent/99999");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DeleteTorrent_unknown_returns_200()
    {
        // The delete endpoint calls Delete and returns Ok() regardless of whether the id exists
        var response = await DeleteAsync("/api/v1/torrent/99999");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
