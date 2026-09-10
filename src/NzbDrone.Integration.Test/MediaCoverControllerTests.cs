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
    public async Task GetMediaCover_returns_404_when_cover_does_not_exist()
    {
        var response = await GetAsync("/api/v1/mediacover/9999/poster.jpg");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
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
