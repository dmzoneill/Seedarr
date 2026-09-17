using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class MediaCoverControllerTests : IntegrationTestBase
{
    [Test]
    public async Task GetMediaCover_returns_placeholder_when_cover_does_not_exist()
    {
        var response = await GetAsync("/api/v1/mediacover/9999/poster.jpg");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/svg+xml"));
    }

    [Test]
    public async Task GetPlaceholder_returns_200_and_svg()
    {
        var response = await GetAsync("/api/v1/mediacover/placeholder?title=TestMovie&category=radarr");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/svg+xml"));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain("TestMovie"));
        Assert.That(content, Does.Contain("🎬"));
    }

    [Test]
    public async Task GetAllMedia_returns_200_and_array()
    {
        var response = await GetAsync("/api/v1/mediacover");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        var list = Deserialize<List<Dictionary<string, object>>>(json);
        Assert.That(list, Is.Not.Null);
    }
}
