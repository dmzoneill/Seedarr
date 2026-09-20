using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Security;

namespace Seedarr.Http.Test.Security;

[TestFixture]
public class CsrfProtectionMiddlewareTest
{
    private IConfigService _config;
    private IConfigFileProvider _configFileProvider;

    [SetUp]
    public void SetUp()
    {
        _config = Substitute.For<IConfigService>();
        _config.CsrfProtectionEnabled.Returns(true);

        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.ApiKey.Returns("valid_master_api_key_123");
    }

    [Test]
    public async Task RequestsWithSeedarrAuthCookie_RequireValidOriginOrReferer_BlocksWhenMissing()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/system/restart";
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["Cookie"] = "Seedarr_Auth=session_token_abc";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task RequestsWithLegacySeedarrAuthCookie_RequireValidOriginOrReferer_BlocksWhenMissing()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/system/restart";
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["Cookie"] = "SeedarrAuth=session_token_legacy";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task RequestsWithSidCookie_OnRpcEndpoint_RequireValidOriginOrReferer_BlocksWhenMissing()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/delete";
        context.Request.Host = new HostString("seedarr.local:8080");
        context.Request.Headers["Cookie"] = "SID=qbittorrent_sid_123";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [TestCase("Seedarr_Auth", "/api/v1/torrents/delete")]
    [TestCase("SeedarrAuth", "/api/v1/torrents/delete")]
    [TestCase("SID", "/api/v2/torrents/delete")]
    [TestCase(".AspNetCore.Cookies", "/api/v1/torrents/delete")]
    public async Task MaliciousOrigin_WithAmbientCookie_IsRejected403Forbidden(string cookieName, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["Cookie"] = $"{cookieName}=some_session_value";
        context.Request.Headers["Origin"] = "http://evil-attacker.com";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task LegitimateOrigin_WithAmbientCookie_IsAllowed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/torrents/pause";
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["Cookie"] = "Seedarr_Auth=legitimate_session";
        context.Request.Headers["Origin"] = "http://seedarr.local:9898";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task LegitimateOrigin_OnRpcEndpoint_WithSidCookie_IsAllowed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/resume";
        context.Request.Host = new HostString("seedarr.local:8080");
        context.Request.Headers["Cookie"] = "SID=qbittorrent_sid";
        context.Request.Headers["Origin"] = "http://seedarr.local:8080";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task LegitimateReferer_WithAmbientCookie_IsAllowed()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/torrents/pause";
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["Cookie"] = "Seedarr_Auth=legitimate_session";
        context.Request.Headers["Referer"] = "http://seedarr.local:9898/torrents";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task NonAmbientApiKeyBypass_RequiresValidKey_ValidHeaderBypasses()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/torrents/delete";
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["X-Api-Key"] = "valid_master_api_key_123";
        context.Request.Headers["Origin"] = "https://external.cross-origin.com";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task NonAmbientApiKeyBypass_RequiresValidKey_BearerTokenBypasses()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/torrents/delete";
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["Authorization"] = "Bearer valid_master_api_key_123";
        context.Request.Headers["Origin"] = "https://external.cross-origin.com";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task NonAmbientApiKeyBypass_RequiresValidKey_InvalidKeyDoesNotBypass()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/torrents/delete";
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["X-Api-Key"] = "invalid_api_key";
        context.Request.Headers["Origin"] = "http://evil-attacker.com";
        context.Request.Headers["Cookie"] = "Seedarr_Auth=session_value";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task QueryParameterApiKey_DoesNotBypassCsrfWhenInvalid()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/torrents/delete";
        context.Request.QueryString = new QueryString("?apikey=attacker_fake_key");
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["Origin"] = "http://evil-attacker.com";
        context.Request.Headers["Cookie"] = "Seedarr_Auth=session_value";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task ReverseProxy_WithHttps_MatchesOriginWithoutPortMismatchRejection()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/settings";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("seedarr.example.com");
        context.Request.Headers["Cookie"] = "Seedarr_Auth=valid_session";
        context.Request.Headers["Origin"] = "https://seedarr.example.com";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task ReverseProxy_WithHttpsPort443_MatchesOriginWithoutPortMismatchRejection()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/settings";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("seedarr.example.com:443");
        context.Request.Headers["Cookie"] = "Seedarr_Auth=valid_session";
        context.Request.Headers["Origin"] = "https://seedarr.example.com";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task ReverseProxy_WithXForwardedHeaders_MatchesOriginWithoutPortMismatchRejection()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/settings";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("seedarr.internal:80");
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "seedarr.example.com";
        context.Request.Headers["Cookie"] = "Seedarr_Auth=valid_session";
        context.Request.Headers["Origin"] = "https://seedarr.example.com";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task RpcPath_WithoutAmbientCookies_BypassesCsrfForAutomatedClients()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v2/torrents/add";
        context.Request.Host = new HostString("seedarr.local:8080");

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, _config, _configFileProvider);

        Assert.That(nextCalled, Is.True);
    }
}
