using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
    public async Task Login_with_XForwardedProto_https_emits_Secure_cookie()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "172.18.0.2");
        request.Headers.Add("X-Forwarded-Host", "seedarr.example.com");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { username = "admin", password = ApiKey, rememberMe = true }),
            Encoding.UTF8,
            "application/json");

        var response = await Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.Contains("Set-Cookie"), Is.True);
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var authCookie = setCookies.FirstOrDefault(c => c.Contains("Seedarr_Auth"));
        Assert.That(authCookie, Is.Not.Null);
        Assert.That(authCookie, Does.Contain("secure").IgnoreCase);
        Assert.That(authCookie, Does.Contain("path=/").IgnoreCase);
    }

    [Test]
    public async Task Login_without_https_emits_non_secure_cookie()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { username = "admin", password = ApiKey, rememberMe = true }),
            Encoding.UTF8,
            "application/json");

        var response = await Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.Contains("Set-Cookie"), Is.True);
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var authCookie = setCookies.FirstOrDefault(c => c.Contains("Seedarr_Auth"));
        Assert.That(authCookie, Is.Not.Null);
        Assert.That(authCookie, Does.Not.Contain("secure").IgnoreCase);
        Assert.That(authCookie, Does.Contain("path=/").IgnoreCase);
    }
}
