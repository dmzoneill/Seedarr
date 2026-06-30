using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    public async Task UploadTorrent_returns_201()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "test.torrent");
        Assume.That(File.Exists(fixturePath), "test.torrent fixture not found");

        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(fixturePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
        content.Add(fileContent, "file", "test.torrent");

        var response = await Client.PostAsync("/api/v1/torrent/upload", content);

        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.Created).Or.EqualTo(HttpStatusCode.Conflict));
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
