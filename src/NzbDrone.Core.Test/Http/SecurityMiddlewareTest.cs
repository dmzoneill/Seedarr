using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Security;

namespace NzbDrone.Core.Test.Http;

[TestFixture]
public class SecurityMiddlewareTest
{
    [TestCase("localhost", true)]
    [TestCase("127.0.0.1", true)]
    [TestCase("::1", true)]
    [TestCase("[::1]", true)]
    [TestCase("192.168.1.100", true)]
    [TestCase("10.0.0.5", true)]
    [TestCase("172.20.0.2", true)]
    [TestCase("169.254.1.1", true)]
    [TestCase("100.64.0.1", true)]
    [TestCase("100.100.50.25", true)]
    [TestCase("100.127.255.254", true)]
    [TestCase("100.63.255.255", false)]
    [TestCase("100.128.0.1", false)]
    [TestCase("fe80::1", true)]
    [TestCase("[fe80::1]", true)]
    [TestCase("fd00::1", true)]
    [TestCase("[fd00::1]", true)]
    [TestCase("fc00::1", true)]
    [TestCase("[fc00::1]", true)]
    [TestCase("fec0::1", true)]
    [TestCase("[fec0::1]", true)]
    [TestCase("2001:db8::1", false)]
    [TestCase("[2001:db8::1]", false)]
    [TestCase("8.8.8.8", false)]
    [TestCase("evil.attacker.com", false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    public void HostHeaderValidation_IsHostAllowed_ValidatesCorrectly(string host, bool expectedAllowed)
    {
        var allowed = HostHeaderValidationMiddleware.IsHostAllowed(host, string.Empty);
        Assert.That(allowed, Is.EqualTo(expectedAllowed));
    }

    [TestCase("sub.example.com", "*.example.com", true)]
    [TestCase("deep.sub.example.com", "*.example.com", true)]
    [TestCase("example.com", "*.example.com", false)]
    [TestCase("badexample.com", "*.example.com", false)]
    [TestCase("sub.local", "*.local", true)]
    [TestCase("myhost.lan", "*.lan", true)]
    [TestCase("app.home.arpa", ".home.arpa", true)]
    [TestCase("any.domain.org", "*", true)]
    [TestCase("my.customdomain.org", "seedarr.local, *.customdomain.org", true)]
    public void HostHeaderValidation_WildcardAllowedHosts(string host, string allowedHosts, bool expectedAllowed)
    {
        var allowed = HostHeaderValidationMiddleware.IsHostAllowed(host, allowedHosts);
        Assert.That(allowed, Is.EqualTo(expectedAllowed));
    }

    [Test]
    public void HostHeaderValidation_AllowsExplicitlyConfiguredDomains()
    {
        var allowed = HostHeaderValidationMiddleware.IsHostAllowed("my.customdomain.org", "seedarr.local, my.customdomain.org");
        Assert.That(allowed, Is.True);
    }

    [TestCase("[fe80::1]")]
    [TestCase("[fd00::1]")]
    [TestCase("100.100.1.1")]
    [TestCase("app.seedarr.lan")]
    public async Task HostHeaderValidationMiddleware_AllowsValidPrivateAndWildcardHostsWhenEnabled(string hostHeader)
    {
        var config = Substitute.For<IConfigService>();
        config.HostHeaderValidationEnabled.Returns(true);
        config.AllowedHosts.Returns("*.seedarr.lan");

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(hostHeader);
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new HostHeaderValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        Assert.That(nextCalled, Is.True);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    [Test]
    public async Task HostHeaderValidationMiddleware_BlocksDisallowedHostWhenEnabled()
    {
        var config = Substitute.For<IConfigService>();
        config.HostHeaderValidationEnabled.Returns(true);
        config.AllowedHosts.Returns(string.Empty);

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("malicious.dnsrebind.com");
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new HostHeaderValidationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
    }

    [Test]
    public async Task CsrfProtectionMiddleware_AllowsSafeGetMethods()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Host = new HostString("localhost:9898");

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task CsrfProtectionMiddleware_BypassesWhenApiKeyIsProvided()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Headers["X-Api-Key"] = "valid_api_key_123";
        context.Request.Headers["Origin"] = "https://external.cross-origin.com";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task CsrfProtectionMiddleware_BlocksCrossOriginSecFetchSite()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task CsrfProtectionMiddleware_BlocksInvalidOrigin()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["Origin"] = "http://evil-attacker.com";
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task CsrfProtectionMiddleware_AllowsMatchingOrigin()
    {
        var config = Substitute.For<IConfigService>();
        config.CsrfProtectionEnabled.Returns(true);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Host = new HostString("seedarr.local:9898");
        context.Request.Headers["Origin"] = "http://seedarr.local:9898";

        var nextCalled = false;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, config);

        Assert.That(nextCalled, Is.True);
    }
}
