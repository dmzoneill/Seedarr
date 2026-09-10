using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
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

    [Test]
    public async Task Announce_with_builtin_tracker_server_successfully_announces_and_updates_status()
    {
        // 1. Enable built-in tracker server on an available dynamic port
        var configResponse = await GetAsync("/api/v1/config/trackerserver");
        Assert.That(configResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        int trackerPort;
        using (var tempListener = new TcpListener(IPAddress.Loopback, 0))
        {
            tempListener.Start();
            trackerPort = ((IPEndPoint)tempListener.LocalEndpoint).Port;
            tempListener.Stop();
        }

        var trackerConfigUpdate = new Dictionary<string, object>
        {
            ["trackerServerEnabled"] = true,
            ["trackerHttpEnabled"] = true,
            ["trackerHttpPort"] = trackerPort,
            ["trackerBindAddress"] = "127.0.0.1",
            ["trackerAnnounceInterval"] = 1800
        };
        var putConfigResponse = await PutJsonAsync("/api/v1/config/trackerserver", trackerConfigUpdate);
        Assert.That(putConfigResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK).Or.EqualTo(HttpStatusCode.Accepted));

        // Wait brief moment for listener to bind
        await Task.Delay(200);

        // 2. Create a torrent
        var createPayload = new Dictionary<string, object>
        {
            ["name"] = "Builtin Tracker Test Torrent",
            ["infoHash"] = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            ["totalSize"] = 1000000L
        };
        var createResponse = await PostJsonAsync("/api/v1/torrent", createPayload);
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = Deserialize<JsonDocument>(createJson);
        var torrentId = createDoc.RootElement.GetProperty("id").GetInt32();

        // 3. Add tracker pointing to built-in tracker server
        var trackerUrl = $"http://127.0.0.1:{trackerPort}/announce";
        var addTrackerPayload = new Dictionary<string, object>
        {
            ["url"] = trackerUrl,
            ["tier"] = 1
        };
        var addTrackerResponse = await PostJsonAsync($"/api/v1/torrent/{torrentId}/trackers", addTrackerPayload);
        Assert.That(addTrackerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // 4. Trigger announce
        var announceResponse = await Client.PostAsync($"/api/v1/torrent/{torrentId}/announce", null);
        Assert.That(announceResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var announceJson = await announceResponse.Content.ReadAsStringAsync();
        using var announceDoc = Deserialize<JsonDocument>(announceJson);
        Assert.That(announceDoc.RootElement.GetProperty("successfulAnnounces").GetInt32(), Is.GreaterThanOrEqualTo(1));

        // 5. Verify tracker entry state
        var getTrackersResponse = await GetAsync($"/api/v1/torrent/{torrentId}/trackers");
        Assert.That(getTrackersResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var trackersJson = await getTrackersResponse.Content.ReadAsStringAsync();
        using var trackersDoc = Deserialize<JsonDocument>(trackersJson);

        Assert.That(trackersDoc.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(1));

        var tracker = trackersDoc.RootElement[0];
        Assert.That(tracker.GetProperty("status").GetString(), Is.EqualTo("Working"));
        Assert.That(tracker.GetProperty("successfulAnnounces").GetInt32(), Is.GreaterThanOrEqualTo(1));
        Assert.That(tracker.GetProperty("consecutiveFailures").GetInt32(), Is.EqualTo(0));
        Assert.That(tracker.GetProperty("lastAnnounce").GetString(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task Announce_with_unreachable_tracker_records_failure_and_increments_consecutive_failures()
    {
        // 1. Create a torrent
        var createPayload = new Dictionary<string, object>
        {
            ["name"] = "Unreachable Tracker Test Torrent",
            ["infoHash"] = "f1e2d3c4b5a6f1e2d3c4b5a6f1e2d3c4b5a6f1e2",
            ["totalSize"] = 500000L
        };
        var createResponse = await PostJsonAsync("/api/v1/torrent", createPayload);
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = Deserialize<JsonDocument>(createJson);
        var torrentId = createDoc.RootElement.GetProperty("id").GetInt32();

        // 2. Add unreachable tracker
        var unreachableUrl = "http://127.0.0.1:59999/announce";
        var addTrackerPayload = new Dictionary<string, object>
        {
            ["url"] = unreachableUrl,
            ["tier"] = 1
        };
        var addTrackerResponse = await PostJsonAsync($"/api/v1/torrent/{torrentId}/trackers", addTrackerPayload);
        Assert.That(addTrackerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // 3. Trigger announce
        var announceResponse = await Client.PostAsync($"/api/v1/torrent/{torrentId}/announce", null);
        Assert.That(announceResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var announceJson = await announceResponse.Content.ReadAsStringAsync();
        using var announceDoc = Deserialize<JsonDocument>(announceJson);
        Assert.That(announceDoc.RootElement.GetProperty("failedAnnounces").GetInt32(), Is.GreaterThanOrEqualTo(1));

        // 4. Verify tracker entry state reflects failure honestly
        var getTrackersResponse = await GetAsync($"/api/v1/torrent/{torrentId}/trackers");
        Assert.That(getTrackersResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var trackersJson = await getTrackersResponse.Content.ReadAsStringAsync();
        using var trackersDoc = Deserialize<JsonDocument>(trackersJson);

        Assert.That(trackersDoc.RootElement.GetArrayLength(), Is.GreaterThanOrEqualTo(1));

        var tracker = trackersDoc.RootElement[0];
        Assert.That(tracker.GetProperty("status").GetString(), Is.EqualTo("Failed"));
        Assert.That(tracker.GetProperty("consecutiveFailures").GetInt32(), Is.GreaterThanOrEqualTo(1));
        Assert.That(tracker.GetProperty("errorMessage").GetString(), Is.Not.Null.And.Not.Empty);
    }
}
