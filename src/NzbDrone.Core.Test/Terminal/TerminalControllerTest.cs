#pragma warning disable SA1117
#pragma warning disable IDE0007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Terminal;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Terminal;

[TestFixture]
public class TerminalControllerTest
{
    private ITerminalService _terminalService;
    private IConfigFileProvider _configFileProvider;
    private TerminalController _controller;

    [SetUp]
    public void SetUp()
    {
        _terminalService = Substitute.For<ITerminalService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.TerminalAccessEnabled.Returns(true);

        _controller = new TerminalController(_terminalService, _configFileProvider);
        SetAdminUser("127.0.0.1", "adminUser");
    }

    private void SetAdminUser(string ipAddress = "127.0.0.1", string username = "adminUser")
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, Roles.Admin)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext
        {
            User = principal
        };
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
        }

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    private void SetNonAdminUser(string role = "User", string username = "regularUser")
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext
        {
            User = principal,
            Connection = { RemoteIpAddress = IPAddress.Parse("10.0.0.5") }
        };

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    private static int? GetStatusCode(IActionResult result)
    {
        if (result is IStatusCodeActionResult statusCodeResult)
        {
            return statusCodeResult.StatusCode;
        }

        return null;
    }

    [Test]
    public void Controller_should_have_Authorize_AdminOnly_attribute()
    {
        var type = typeof(TerminalController);
        var attr = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;

        Assert.That(attr, Is.Not.Null);
        Assert.That(attr.Policy, Is.EqualTo(Policies.AdminOnly));
    }

    [Test]
    public async Task StartSession_should_return_403_when_TerminalAccessEnabled_is_false()
    {
        _configFileProvider.TerminalAccessEnabled.Returns(false);

        var result = await _controller.StartSession();

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status403Forbidden));
        await _terminalService.DidNotReceive().StartSessionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<Func<string, Task>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void GetStatus_should_return_403_when_TerminalAccessEnabled_is_false()
    {
        _configFileProvider.TerminalAccessEnabled.Returns(false);

        var result = _controller.GetStatus();

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public void Resize_should_return_403_when_TerminalAccessEnabled_is_false()
    {
        _configFileProvider.TerminalAccessEnabled.Returns(false);

        var result = _controller.Resize(new TerminalResizeRequest { ConnectionId = "conn-1", Cols = 80, Rows = 24 });

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status403Forbidden));
        _terminalService.DidNotReceive().Resize(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Test]
    public async Task CloseSession_should_return_403_when_TerminalAccessEnabled_is_false()
    {
        _configFileProvider.TerminalAccessEnabled.Returns(false);

        var result = await _controller.CloseSession("conn-1");

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status403Forbidden));
        await _terminalService.DidNotReceive().CloseSessionAsync(Arg.Any<string>());
    }

    [Test]
    public async Task StartSession_should_return_403_when_user_is_not_admin()
    {
        SetNonAdminUser();

        var result = await _controller.StartSession();

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status403Forbidden));
        await _terminalService.DidNotReceive().StartSessionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<Func<string, Task>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void GetStatus_should_return_403_when_user_is_not_admin()
    {
        SetNonAdminUser();

        var result = _controller.GetStatus();

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public void Resize_should_return_403_when_user_is_not_admin()
    {
        SetNonAdminUser();

        var result = _controller.Resize(new TerminalResizeRequest { ConnectionId = "conn-1", Cols = 80, Rows = 24 });

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [Test]
    public async Task CloseSession_should_return_403_when_user_is_not_admin()
    {
        SetNonAdminUser();

        var result = await _controller.CloseSession("conn-1");

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    [TestCase(9, 24)]
    [TestCase(501, 24)]
    [TestCase(80, 4)]
    [TestCase(80, 201)]
    [TestCase(-1, 24)]
    [TestCase(80, -5)]
    [TestCase(0, 0)]
    public async Task StartSession_should_return_400_for_invalid_dimensions(int cols, int rows)
    {
        var request = new TerminalSessionRequest { Cols = cols, Rows = rows };

        var result = await _controller.StartSession(request);

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status400BadRequest));
        await _terminalService.DidNotReceive().StartSessionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<Func<string, Task>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>());
    }

    [TestCase(9, 24)]
    [TestCase(501, 24)]
    [TestCase(80, 4)]
    [TestCase(80, 201)]
    public void Resize_should_return_400_for_invalid_dimensions(int cols, int rows)
    {
        var request = new TerminalResizeRequest { ConnectionId = "conn-1", Cols = cols, Rows = rows };

        var result = _controller.Resize(request);

        Assert.That(GetStatusCode(result), Is.EqualTo(StatusCodes.Status400BadRequest));
        _terminalService.DidNotReceive().Resize(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Test]
    public async Task StartSession_should_succeed_and_start_session_for_valid_admin_request()
    {
        var request = new TerminalSessionRequest { ConnectionId = "my-conn", Cols = 120, Rows = 40 };

        var result = await _controller.StartSession(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        var resource = okResult.Value as TerminalSessionResource;

        Assert.That(resource, Is.Not.Null);
        Assert.That(resource.ConnectionId, Is.EqualTo("my-conn"));
        Assert.That(resource.Cols, Is.EqualTo(120));
        Assert.That(resource.Rows, Is.EqualTo(40));

        await _terminalService.Received(1).StartSessionAsync(
            "my-conn", 120, 40,
            Arg.Any<Func<string, Task>>(), Arg.Any<Action>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void GetStatus_should_return_status_and_active_sessions_for_admin()
    {
        _terminalService.ActiveSessionIds.Returns(new[] { "conn-1", "conn-2" });

        var result = _controller.GetStatus();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        var resource = okResult.Value as TerminalStatusResource;

        Assert.That(resource, Is.Not.Null);
        Assert.That(resource.TerminalAccessEnabled, Is.True);
        Assert.That(resource.ActiveSessions, Is.EquivalentTo(new[] { "conn-1", "conn-2" }));
    }

    [Test]
    public void Resize_should_succeed_for_valid_admin_request()
    {
        var request = new TerminalResizeRequest { ConnectionId = "conn-1", Cols = 100, Rows = 30 };

        var result = _controller.Resize(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _terminalService.Received(1).Resize("conn-1", 100, 30);
    }

    [Test]
    public async Task CloseSession_should_succeed_for_admin()
    {
        var result = await _controller.CloseSession("conn-1");

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        await _terminalService.Received(1).CloseSessionAsync("conn-1");
    }

    [Test]
    public void TerminalEnvironmentSanitizer_strips_sensitive_variables_and_prefixes()
    {
        var input = new Dictionary<string, string>
        {
            { "SEEDARR_API_KEY", "super-secret-seedarr-key" },
            { "SEEDARR_CUSTOM_CONFIG", "seedarr-val" },
            { "NZBDRONE_API_KEY", "super-secret-nzbdrone-key" },
            { "NZBDRONE_BRANCH", "master" },
            { "OIDC_CLIENT_SECRET", "oidc-secret-val" },
            { "OIDC_ISSUER", "https://auth.example.com" },
            { "DB_PASSWORD", "postgres-pass" },
            { "DB_HOST", "localhost" },
            { "DATABASE_URL", "postgres://user:pass@host/db" },
            { "MY_SUPER_SECRET_TOKEN", "token123" },
            { "APP_APIKEY", "apikey123" },
            { "USER_PASSWORD", "mypass" },
            { "AWS_CREDENTIALS", "cred" }
        };

        var sanitized = TerminalEnvironmentSanitizer.Sanitize(input);

        Assert.That(sanitized.ContainsKey("SEEDARR_API_KEY"), Is.False);
        Assert.That(sanitized.ContainsKey("SEEDARR_CUSTOM_CONFIG"), Is.False);
        Assert.That(sanitized.ContainsKey("NZBDRONE_API_KEY"), Is.False);
        Assert.That(sanitized.ContainsKey("NZBDRONE_BRANCH"), Is.False);
        Assert.That(sanitized.ContainsKey("OIDC_CLIENT_SECRET"), Is.False);
        Assert.That(sanitized.ContainsKey("OIDC_ISSUER"), Is.False);
        Assert.That(sanitized.ContainsKey("DB_PASSWORD"), Is.False);
        Assert.That(sanitized.ContainsKey("DB_HOST"), Is.False);
        Assert.That(sanitized.ContainsKey("DATABASE_URL"), Is.False);
        Assert.That(sanitized.ContainsKey("MY_SUPER_SECRET_TOKEN"), Is.False);
        Assert.That(sanitized.ContainsKey("APP_APIKEY"), Is.False);
        Assert.That(sanitized.ContainsKey("USER_PASSWORD"), Is.False);
        Assert.That(sanitized.ContainsKey("AWS_CREDENTIALS"), Is.False);
    }

    [Test]
    public void TerminalEnvironmentSanitizer_retains_whitelisted_runtime_variables()
    {
        var input = new Dictionary<string, string>
        {
            { "TERM", "xterm" },
            { "COLORTERM", "truecolor" },
            { "LANG", "en_GB.UTF-8" },
            { "PATH", "/usr/bin:/bin" },
            { "HOME", "/home/seedarr" },
            { "USER", "seedarr" },
            { "SEEDARR_SECRET", "should-be-stripped" }
        };

        var sanitized = TerminalEnvironmentSanitizer.Sanitize(input);

        Assert.That(sanitized["TERM"], Is.EqualTo("xterm"));
        Assert.That(sanitized["COLORTERM"], Is.EqualTo("truecolor"));
        Assert.That(sanitized["LANG"], Is.EqualTo("en_GB.UTF-8"));
        Assert.That(sanitized["PATH"], Is.EqualTo("/usr/bin:/bin"));
        Assert.That(sanitized["HOME"], Is.EqualTo("/home/seedarr"));
        Assert.That(sanitized["USER"], Is.EqualTo("seedarr"));
        Assert.That(sanitized.ContainsKey("SEEDARR_SECRET"), Is.False);
    }

    [Test]
    public void TerminalEnvironmentSanitizer_sets_standard_defaults_for_TERM_and_LANG_when_missing()
    {
        var input = new Dictionary<string, string>
        {
            { "USER", "testuser" }
        };

        var sanitized = TerminalEnvironmentSanitizer.Sanitize(input);

        Assert.That(sanitized["TERM"], Is.EqualTo(TerminalEnvironmentSanitizer.DefaultTerm));
        Assert.That(sanitized["LANG"], Is.EqualTo(TerminalEnvironmentSanitizer.DefaultLang));
        Assert.That(sanitized["USER"], Is.EqualTo("testuser"));
    }
}
