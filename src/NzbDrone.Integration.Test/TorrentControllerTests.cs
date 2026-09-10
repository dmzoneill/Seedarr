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
    [SetUp]
    public async Task CleanupTorrents()
    {
        var response = await GetAsync("/api/v1/torrent");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var torrents = Deserialize<List<Dictionary<string, object>>>(json);

        foreach (var torrent in torrents ?? [])
        {
            await DeleteAsync($"/api/v1/torrent/{torrent["id"]}");
        }
    }

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

    [Test]
    public async Task Put_updates_user_fields_without_clobbering_engine_stats()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "test.torrent");
        Assume.That(File.Exists(fixturePath), "test.torrent fixture not found");

        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(fixturePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
        content.Add(fileContent, "file", "put-invariant-test.torrent");

        var uploadResponse = await Client.PostAsync("/api/v1/torrent/upload", content);
        Assume.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = Deserialize<JsonDocument>(uploadJson);
        var torrentId = uploadDoc.RootElement.GetProperty("added")[0].GetProperty("id").GetInt32();

        // Get the existing torrent
        var getResponse = await GetAsync($"/api/v1/torrent/{torrentId}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var originalJson = await getResponse.Content.ReadAsStringAsync();
        using var originalDoc = Deserialize<JsonDocument>(originalJson);
        var infoHash = originalDoc.RootElement.GetProperty("infoHash").GetString();

        // Perform PUT with updated user fields and malicious stats payload
        var updatePayload = new Dictionary<string, object>
        {
            ["id"] = torrentId,
            ["name"] = "Updated Torrent Name",
            ["priority"] = 5,
            ["uploadLimit"] = 1048576,
            ["downloadLimit"] = 2097152,
            ["label"] = "MyCategory",
            // Attempt to mutate engine-owned fields (should be ignored)
            ["infoHash"] = "malicious_clobber_hash",
            ["uploaded"] = 999999999L,
            ["downloaded"] = 888888888L,
            ["seeders"] = 1234,
            ["leechers"] = 5678,
            ["sessionUploaded"] = 777777L
        };

        var putResponse = await PutJsonAsync($"/api/v1/torrent/{torrentId}", updatePayload);
        Assert.That(putResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var putJson = await putResponse.Content.ReadAsStringAsync();
        using var putDoc = Deserialize<JsonDocument>(putJson);

        // User controllable fields should be updated
        Assert.That(putDoc.RootElement.GetProperty("name").GetString(), Is.EqualTo("Updated Torrent Name"));
        Assert.That(putDoc.RootElement.GetProperty("priority").GetInt32(), Is.EqualTo(5));
        Assert.That(putDoc.RootElement.GetProperty("uploadLimit").GetInt32(), Is.EqualTo(1048576));
        Assert.That(putDoc.RootElement.GetProperty("downloadLimit").GetInt32(), Is.EqualTo(2097152));
        Assert.That(putDoc.RootElement.GetProperty("label").GetString(), Is.EqualTo("MyCategory"));

        // Engine-owned / immutable fields should NOT be clobbered
        Assert.That(putDoc.RootElement.GetProperty("infoHash").GetString(), Is.EqualTo(infoHash));
        Assert.That(putDoc.RootElement.GetProperty("uploaded").GetInt64(), Is.EqualTo(0L));
        Assert.That(putDoc.RootElement.GetProperty("downloaded").GetInt64(), Is.EqualTo(0L));
        Assert.That(putDoc.RootElement.GetProperty("seeders").GetInt32(), Is.EqualTo(0));
        Assert.That(putDoc.RootElement.GetProperty("leechers").GetInt32(), Is.EqualTo(0));
        Assert.That(putDoc.RootElement.GetProperty("sessionUploaded").GetInt64(), Is.EqualTo(0L));
    }
}
