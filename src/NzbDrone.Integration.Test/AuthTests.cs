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

    [Test]
    public async Task Login_with_empty_password_returns_400_with_harmonized_error()
    {
        var response = await PostJsonAsync("/api/v1/auth/login", new { Username = "admin", Password = "" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain("Password or API key is required"));
    }
}
