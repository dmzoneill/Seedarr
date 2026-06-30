using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("IntegrationTest")]
public class AuthTests : IntegrationTestBase
{
    [Test]
    public async Task SystemStatus_with_no_headers_returns_200_when_auth_disabled()
    {
        // AuthenticationEnabled defaults to false in ConfigFileProvider
        var response = await GetAsync("/api/v1/system/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task GetTorrents_with_no_headers_returns_200_when_auth_disabled()
    {
        var response = await GetAsync("/api/v1/torrent");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
