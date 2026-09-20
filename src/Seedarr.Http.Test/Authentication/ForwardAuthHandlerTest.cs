// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Authentication;

namespace Seedarr.Http.Test.Authentication;

[TestFixture]
public class ForwardAuthHandlerTest
{
    private IConfigFileProvider _configFileProvider;
    private IOptionsMonitor<ForwardAuthOptions> _optionsMonitor;
    private ForwardAuthOptions _options;
    private ILoggerFactory _loggerFactory;
    private UrlEncoder _encoder;

    [SetUp]
    public void SetUp()
    {
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _options = new ForwardAuthOptions();
        _optionsMonitor = Substitute.For<IOptionsMonitor<ForwardAuthOptions>>();
        _optionsMonitor.Get(Arg.Any<string>()).Returns(_options);
        _optionsMonitor.CurrentValue.Returns(_options);

        _loggerFactory = Substitute.For<ILoggerFactory>();
        _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());
        _encoder = UrlEncoder.Default;
    }

    private async Task<AuthenticateResult> AuthenticateAsync(HttpContext context)
    {
        var handler = new ForwardAuthHandler(_optionsMonitor, _loggerFactory, _encoder, _configFileProvider);
        var scheme = new AuthenticationScheme(ForwardAuthOptions.DefaultScheme, null, typeof(ForwardAuthHandler));
        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpIsNull_ReturnsNoResult()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;
        context.Request.Headers["X-Forwarded-User"] = "attacker";

        var result = await AuthenticateAsync(context);

        Assert.That(result.None, Is.True);
        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpIsUntrusted_ReturnsNoResult()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.25");
        context.Request.Headers["X-Forwarded-User"] = "attacker";

        var result = await AuthenticateAsync(context);

        Assert.That(result.None, Is.True);
        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpIsUntrustedWithSpoofedAuthentikHeader_ReturnsNoResult()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        context.Request.Headers["X-authentik-username"] = "admin";
        context.Request.Headers["X-authentik-groups"] = "admin";

        var result = await AuthenticateAsync(context);

        Assert.That(result.None, Is.True);
        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpIsLoopback_Succeeds()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-Forwarded-User"] = "validuser";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("validuser"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpIsIPv6Loopback_Succeeds()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.IPv6Loopback;
        context.Request.Headers["Remote-User"] = "alice";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("alice"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpIsIPv4MappedLoopback_Succeeds()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:127.0.0.1");
        context.Request.Headers["X-Forwarded-User"] = "bob";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("bob"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpMatchesOptionsTrustedProxies_Succeeds()
    {
        _options.TrustedProxies = "10.0.0.2";
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
        context.Request.Headers["X-Forwarded-User"] = "proxyuser";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("proxyuser"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpMatchesConfigFileTrustedProxiesCidr_Succeeds()
    {
        _configFileProvider.TrustedProxies.Returns("172.16.0.0/12");
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.16.5.10");
        context.Request.Headers["X-Forwarded-User"] = "cidruser";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("cidruser"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenRemoteIpOutsideConfigFileTrustedProxiesCidr_ReturnsNoResult()
    {
        _configFileProvider.TrustedProxies.Returns("172.16.0.0/12");
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.32.1.1");
        context.Request.Headers["X-Forwarded-User"] = "outsideuser";

        var result = await AuthenticateAsync(context);

        Assert.That(result.None, Is.True);
        Assert.That(result.Succeeded, Is.False);
    }

    [TestCase("admin")]
    [TestCase("admins")]
    [TestCase("administrator")]
    [TestCase("administrators")]
    [TestCase("other,admin,guest")]
    public async Task HandleAuthenticateAsync_WhenUserHasAdminGroup_AssignsAdminRole(string groups)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-Forwarded-User"] = "adminuser";
        context.Request.Headers["X-Forwarded-Groups"] = groups;

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        var role = result.Principal?.FindFirst(ClaimTypes.Role)?.Value;
        Assert.That(role, Is.EqualTo("Admin"));
    }

    [TestCase("users")]
    [TestCase("viewer")]
    [TestCase("readonly")]
    [TestCase("media-consumers")]
    public async Task HandleAuthenticateAsync_WhenUserHasNonAdminGroup_AssignsUserRole(string groups)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-Forwarded-User"] = "regularuser";
        context.Request.Headers["X-Forwarded-Groups"] = groups;

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        var role = result.Principal?.FindFirst(ClaimTypes.Role)?.Value;
        Assert.That(role, Is.EqualTo("User"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenUserHasNoGroupsHeader_DefaultsToUserRole()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-Forwarded-User"] = "nogroupuser";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        var role = result.Principal?.FindFirst(ClaimTypes.Role)?.Value;
        Assert.That(role, Is.EqualTo("User"));
    }

    [Test]
    public async Task HandleAuthenticateAsync_WhenNoUsernameHeader_ReturnsNoResult()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        var result = await AuthenticateAsync(context);

        Assert.That(result.None, Is.True);
    }

    [Test]
    public async Task HandleAuthenticateAsync_MapsEmailAndDisplayNameAndGroups()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-authentik-username"] = "john.doe";
        context.Request.Headers["X-authentik-name"] = "John Doe";
        context.Request.Headers["X-authentik-email"] = "john@example.com";
        context.Request.Headers["X-authentik-groups"] = "family,streamers";

        var result = await AuthenticateAsync(context);

        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Principal?.Identity?.Name, Is.EqualTo("john.doe"));
        Assert.That(result.Principal?.FindFirst("DisplayName")?.Value, Is.EqualTo("John Doe"));
        Assert.That(result.Principal?.FindFirst(ClaimTypes.Email)?.Value, Is.EqualTo("john@example.com"));

        var groupClaims = result.Principal?.FindAll("Group").Select(c => c.Value).ToList();
        Assert.That(groupClaims, Does.Contain("family"));
        Assert.That(groupClaims, Does.Contain("streamers"));
    }
}
