#pragma warning disable SA1117
#pragma warning disable IDE0007

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Terminal;
using Seedarr.Http.Terminal;

namespace Seedarr.Http.Test.SignalR;

[TestFixture]
public class TerminalHubAuthTest
{
    private IConfigFileProvider _configFileProvider;
    private ITerminalService _terminalService;
    private HubCallerContext _callerContext;
    private DefaultHttpContext _httpContext;
    private FeatureCollection _features;

    [SetUp]
    public void SetUp()
    {
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _terminalService = Substitute.For<ITerminalService>();
        _callerContext = Substitute.For<HubCallerContext>();

        _callerContext.ConnectionId.Returns("terminal-conn-1");

        _httpContext = new DefaultHttpContext();
        _features = new FeatureCollection();
        var httpContextFeature = Substitute.For<IHttpContextFeature>();
        httpContextFeature.HttpContext.Returns(_httpContext);
        _features.Set<IHttpContextFeature>(httpContextFeature);
        _callerContext.Features.Returns(_features);
    }

    private TerminalHub CreateHub()
    {
        return new TerminalHub(_terminalService, _configFileProvider)
        {
            Context = _callerContext
        };
    }

    [Test]
    public void TerminalHub_has_AuthorizeAttribute_with_AdminOnly_policy()
    {
        var type = typeof(TerminalHub);
        var attr = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.AdminOnly));
    }

    [Test]
    public async Task OnConnectedAsync_allows_connection_when_authentication_disabled()
    {
        _configFileProvider.AuthenticationEnabled.Returns(false);
        _configFileProvider.TerminalAccessEnabled.Returns(true);

        await CreateHub().OnConnectedAsync();

        _callerContext.DidNotReceive().Abort();
    }

    [Test]
    public async Task OnConnectedAsync_allows_admin_authenticated_user()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.TerminalAccessEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, Roles.Admin)
            },
            "Cookies");
        _callerContext.User.Returns(new ClaimsPrincipal(identity));

        await CreateHub().OnConnectedAsync();

        _callerContext.DidNotReceive().Abort();
    }

    [Test]
    public async Task OnConnectedAsync_aborts_non_admin_authenticated_user()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.TerminalAccessEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "readonly"),
                new Claim(ClaimTypes.Role, Roles.ReadOnly)
            },
            "Cookies");
        _callerContext.User.Returns(new ClaimsPrincipal(identity));

        await CreateHub().OnConnectedAsync();

        _callerContext.Received(1).Abort();
    }

    [Test]
    public async Task OnConnectedAsync_allows_connection_via_valid_api_key_without_principal_roles()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.TerminalAccessEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("valid-master-api-key");

        _httpContext.Request.QueryString = new QueryString("?access_token=valid-master-api-key");

        await CreateHub().OnConnectedAsync();

        _callerContext.DidNotReceive().Abort();
    }

    [Test]
    public async Task OnConnectedAsync_aborts_when_authentication_enabled_and_http_context_missing()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.TerminalAccessEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var httpContextFeature = Substitute.For<IHttpContextFeature>();
        httpContextFeature.HttpContext.Returns((HttpContext)null);
        _features.Set<IHttpContextFeature>(httpContextFeature);

        await CreateHub().OnConnectedAsync();

        _callerContext.Received(1).Abort();
    }

    [Test]
    public async Task OnConnectedAsync_aborts_readonly_principal_restored_on_http_context_via_AuthenticateAsync()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.TerminalAccessEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "readonly"),
                new Claim(ClaimTypes.Role, Roles.ReadOnly)
            },
            "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Cookies");

        var authService = Substitute.For<IAuthenticationService>();
        authService.AuthenticateAsync(_httpContext, "Cookies")
            .Returns(Task.FromResult(AuthenticateResult.Success(ticket)));

        var schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
        schemeProvider.GetDefaultAuthenticateSchemeAsync()
            .Returns(Task.FromResult<AuthenticationScheme>(new AuthenticationScheme("Cookies", "Cookies", typeof(IAuthenticationHandler))));

        var services = new ServiceCollection();
        services.AddSingleton(authService);
        services.AddSingleton(schemeProvider);
        _httpContext.RequestServices = services.BuildServiceProvider();

        await CreateHub().OnConnectedAsync();

        _callerContext.Received(1).Abort();
    }

    [Test]
    public async Task OnConnectedAsync_allows_admin_principal_restored_on_http_context_via_AuthenticateAsync()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.TerminalAccessEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, Roles.Admin)
            },
            "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Cookies");

        var authService = Substitute.For<IAuthenticationService>();
        authService.AuthenticateAsync(_httpContext, "Cookies")
            .Returns(Task.FromResult(AuthenticateResult.Success(ticket)));

        var schemeProvider = Substitute.For<IAuthenticationSchemeProvider>();
        schemeProvider.GetDefaultAuthenticateSchemeAsync()
            .Returns(Task.FromResult<AuthenticationScheme>(new AuthenticationScheme("Cookies", "Cookies", typeof(IAuthenticationHandler))));

        var services = new ServiceCollection();
        services.AddSingleton(authService);
        services.AddSingleton(schemeProvider);
        _httpContext.RequestServices = services.BuildServiceProvider();

        await CreateHub().OnConnectedAsync();

        _callerContext.DidNotReceive().Abort();
    }

    [Test]
    public async Task OnConnectedAsync_aborts_when_TerminalAccessEnabled_is_false()
    {
        _configFileProvider.AuthenticationEnabled.Returns(false);
        _configFileProvider.TerminalAccessEnabled.Returns(false);

        await CreateHub().OnConnectedAsync();

        _callerContext.Received(1).Abort();
    }

    [Test]
    public void StartSession_throws_when_TerminalAccessEnabled_is_false()
    {
        _configFileProvider.TerminalAccessEnabled.Returns(false);

        var hub = CreateHub();

        Assert.ThrowsAsync<HubException>(async () => await hub.StartSession(80, 24));
        _terminalService.DidNotReceive().StartSessionAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<Func<string, Task>>(),
            Arg.Any<Action>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void WriteInput_throws_when_TerminalAccessEnabled_is_false()
    {
        _configFileProvider.TerminalAccessEnabled.Returns(false);

        var hub = CreateHub();

        Assert.ThrowsAsync<HubException>(async () => await hub.WriteInput("ls"));
        _terminalService.DidNotReceive().WriteInputAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void Resize_throws_when_TerminalAccessEnabled_is_false()
    {
        _configFileProvider.TerminalAccessEnabled.Returns(false);

        var hub = CreateHub();

        Assert.Throws<HubException>(() => hub.Resize(120, 40));
        _terminalService.DidNotReceive().Resize(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }
}
