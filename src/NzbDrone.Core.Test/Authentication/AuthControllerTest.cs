using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Auth;
using Roles = NzbDrone.Core.Authentication.Roles;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class AuthControllerTest
{
    private IIdentityProviderService _identityProviderService;
    private IConfigFileProvider _configFileProvider;
    private ISessionRevocationService _sessionRevocationService;
    private ILoginRateLimiter _loginRateLimiter;
    private AuthController _controller;

    [SetUp]
    public void SetUp()
    {
        _identityProviderService = Substitute.For<IIdentityProviderService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _sessionRevocationService = Substitute.For<ISessionRevocationService>();
        _loginRateLimiter = new LoginRateLimiter();
        _controller = new AuthController(_identityProviderService, _configFileProvider, _sessionRevocationService, _loginRateLimiter);
    }

    [Test]
    public async Task Login_WhenRequestIsNull_ReturnsBadRequestWithHarmonizedMessage()
    {
        var result = await _controller.Login(null);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var error = badRequest.Value.GetType().GetProperty("error")?.GetValue(badRequest.Value) as string;
        Assert.That(error, Is.EqualTo("Password or API key is required"));
    }

    [Test]
    public async Task Login_WhenPasswordIsEmpty_ReturnsBadRequestWithHarmonizedMessage()
    {
        var request = new LoginRequestResource { Username = "admin", Password = "" };

        var result = await _controller.Login(request);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var error = badRequest.Value.GetType().GetProperty("error")?.GetValue(badRequest.Value) as string;
        Assert.That(error, Is.EqualTo("Password or API key is required"));
    }

    [Test]
    public async Task Login_WhenPasswordIsWhitespace_ReturnsBadRequestWithHarmonizedMessage()
    {
        var request = new LoginRequestResource { Username = "admin", Password = "   " };

        var result = await _controller.Login(request);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var error = badRequest.Value.GetType().GetProperty("error")?.GetValue(badRequest.Value) as string;
        Assert.That(error, Is.EqualTo("Password or API key is required"));
    }

    [Test]
    public async Task Login_WhenAuthenticationEnabledAndCredentialsInvalid_ReturnsUnauthorizedWithHarmonizedMessage()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("correct-api-key");

        var request = new LoginRequestResource { Username = "admin", Password = "wrong-password" };

        var result = await _controller.Login(request);

        Assert.That(result.Result, Is.TypeOf<UnauthorizedObjectResult>());
        var unauthorized = (UnauthorizedObjectResult)result.Result;
        var error = unauthorized.Value.GetType().GetProperty("error")?.GetValue(unauthorized.Value) as string;
        Assert.That(error, Is.EqualTo("Invalid credentials. Please verify your username and password or API key."));
    }

    [Test]
    public async Task Login_WhenApiKeyInUsernameAndInvalidPassword_ReturnsUnauthorized()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var request = new LoginRequestResource { Username = "master-api-key", Password = "wrong-password" };

        var result = await _controller.Login(request);

        Assert.That(result.Result, Is.TypeOf<UnauthorizedObjectResult>());
        var unauthorized = (UnauthorizedObjectResult)result.Result;
        var error = unauthorized.Value.GetType().GetProperty("error")?.GetValue(unauthorized.Value) as string;
        Assert.That(error, Is.EqualTo("Invalid credentials. Please verify your username and password or API key."));
    }

    [Test]
    public async Task Login_WhenValidLoginWithApiKeyInUsername_SanitizesUsernameClaimsAndDoesNotExposeApiKey()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("super-secret-api-key-999");

        var httpContext = new DefaultHttpContext();
        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        ClaimsPrincipal capturedPrincipal = null;
        await authService.SignInAsync(
            httpContext,
            "Cookies",
            Arg.Do<ClaimsPrincipal>(p => capturedPrincipal = p),
            Arg.Any<AuthenticationProperties>());

        var request = new LoginRequestResource
        {
            Username = "super-secret-api-key-999",
            Password = "super-secret-api-key-999",
        };

        var result = await _controller.Login(request);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var user = okResult.Value as CurrentUserResource;

        Assert.That(user, Is.Not.Null);
        Assert.That(user.Username, Is.EqualTo("admin"));
        Assert.That(user.DisplayName, Is.EqualTo("Administrator"));
        Assert.That(user.Identifier, Is.EqualTo("admin"));

        Assert.That(capturedPrincipal, Is.Not.Null);
        Assert.That(capturedPrincipal.FindFirst(ClaimTypes.Name)?.Value, Is.EqualTo("admin"));
        Assert.That(capturedPrincipal.FindFirst("DisplayName")?.Value, Is.EqualTo("Administrator"));
        Assert.That(capturedPrincipal.Claims.Any(c => c.Value.Contains("super-secret-api-key-999")), Is.False);
    }

    [Test]
    public async Task Login_WhenValidLoginWithLegitimateUsername_PreservesUsernameInClaims()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("super-secret-api-key-999");

        var httpContext = new DefaultHttpContext();
        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        ClaimsPrincipal capturedPrincipal = null;
        await authService.SignInAsync(
            httpContext,
            "Cookies",
            Arg.Do<ClaimsPrincipal>(p => capturedPrincipal = p),
            Arg.Any<AuthenticationProperties>());

        var request = new LoginRequestResource
        {
            Username = "custom_operator",
            Password = "super-secret-api-key-999",
        };

        var result = await _controller.Login(request);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var user = okResult.Value as CurrentUserResource;

        Assert.That(user, Is.Not.Null);
        Assert.That(user.Username, Is.EqualTo("custom_operator"));
        Assert.That(user.DisplayName, Is.EqualTo("custom_operator"));

        Assert.That(capturedPrincipal, Is.Not.Null);
        Assert.That(capturedPrincipal.FindFirst(ClaimTypes.Name)?.Value, Is.EqualTo("custom_operator"));
        Assert.That(capturedPrincipal.FindFirst("DisplayName")?.Value, Is.EqualTo("custom_operator"));
        Assert.That(capturedPrincipal.Claims.Any(c => c.Value.Contains("super-secret-api-key-999")), Is.False);
    }

    [Test]
    public void GetProviders_ReturnsConfiguredProviders()
    {
        var providers = new List<IdentityProviderDefinition>
        {
            new() { Id = 1, ProviderId = "oidc1", Name = "OIDC 1", ProviderType = IdentityProviderType.Oidc, IconUrl = "/icons/oidc1.png", ButtonText = "Login with OIDC" },
        };
        _identityProviderService.GetEnabled().Returns(providers);

        var actionResult = _controller.GetProviders();

        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var ok = (OkObjectResult)actionResult.Result;
        var list = ok.Value as List<AuthProviderResource>;
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].ProviderId, Is.EqualTo("oidc1"));
        Assert.That(list[0].ButtonText, Is.EqualTo("Login with OIDC"));
    }

    [Test]
    public void GetProviders_WithReturnUrl_PreservesReturnUrlInLoginUrl()
    {
        var providers = new List<IdentityProviderDefinition>
        {
            new() { Id = 1, ProviderId = "keycloak", Name = "Keycloak", ProviderType = IdentityProviderType.Oidc },
        };
        _identityProviderService.GetEnabled().Returns(providers);

        var actionResult = _controller.GetProviders("/torrents/details?id=42");

        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var ok = (OkObjectResult)actionResult.Result;
        var list = ok.Value as List<AuthProviderResource>;
        Assert.That(list, Is.Not.Null);
        Assert.That(list[0].LoginUrl, Is.EqualTo("/api/v1/auth/login/keycloak?returnUrl=%2Ftorrents%2Fdetails%3Fid%3D42"));
    }

    [Test]
    public void GetProviders_WithMaliciousReturnUrl_SanitizesReturnUrlInLoginUrl()
    {
        var providers = new List<IdentityProviderDefinition>
        {
            new() { Id = 1, ProviderId = "google", Name = "Google", ProviderType = IdentityProviderType.Oidc },
        };
        _identityProviderService.GetEnabled().Returns(providers);

        var actionResult = _controller.GetProviders("http://evil.com");

        Assert.That(actionResult.Result, Is.TypeOf<OkObjectResult>());
        var ok = (OkObjectResult)actionResult.Result;
        var list = ok.Value as List<AuthProviderResource>;
        Assert.That(list, Is.Not.Null);
        Assert.That(list[0].LoginUrl, Is.EqualTo("/api/v1/auth/login/google?returnUrl=%2F"));
    }

    [Test]
    public async Task Logout_WhenUserIsAuthenticated_RevokesSessionAndUsername()
    {
        var httpContext = new DefaultHttpContext();
        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "testuser"),
            new("SessionId", "session-xyz-123"),
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _controller.Logout();

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        _sessionRevocationService.Received(1).RevokeSession("session-xyz-123");
        _sessionRevocationService.Received(1).RevokeSession("testuser");
        await authService.Received(1).SignOutAsync(httpContext, "Cookies", Arg.Any<AuthenticationProperties>());
    }

    [Test]
    public void SessionRevocationService_RevokesAndIdentifiesRevokedSession()
    {
        var service = new SessionRevocationService();
        var now = DateTime.UtcNow;

        service.RevokeSession("session-1", now);

        Assert.That(service.IsSessionRevoked("session-1", now.AddMinutes(-10)), Is.True);
        Assert.That(service.IsSessionRevoked("session-1", now), Is.True);
        Assert.That(service.IsSessionRevoked("session-1", now.AddMinutes(5)), Is.False);
        Assert.That(service.IsSessionRevoked("session-2", now.AddMinutes(-10)), Is.False);
    }

    [Test]
    public void SessionRevocationService_ClearExpired_RemovesOldEntries()
    {
        var service = new SessionRevocationService();
        var oldTime = DateTime.UtcNow.AddDays(-40);

        service.RevokeSession("session-old", oldTime);
        Assert.That(service.IsSessionRevoked("session-old", oldTime), Is.True);

        service.ClearExpired(TimeSpan.FromDays(30));

        Assert.That(service.IsSessionRevoked("session-old", oldTime), Is.False);
    }

    [Test]
    public async Task OnValidatePrincipal_WhenSessionIsRevoked_RejectsPrincipalAndSignsOut()
    {
        var revocationService = new SessionRevocationService();
        revocationService.RevokeSession("session-123");

        var httpContext = new DefaultHttpContext();
        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ISessionRevocationService)).Returns(revocationService);
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "testuser"),
            new("SessionId", "session-123"),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
        var authProps = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
        };
        var ticket = new AuthenticationTicket(principal, authProps, "Cookies");
        var scheme = new AuthenticationScheme("Cookies", "Cookies", typeof(CookieAuthenticationHandler));
        var options = new CookieAuthenticationOptions();
        var context = new CookieValidatePrincipalContext(httpContext, scheme, options, ticket);

        var issuedUtc = context.Properties.IssuedUtc?.UtcDateTime ?? DateTime.MinValue;
        var sessionId = context.Principal?.FindFirst("SessionId")?.Value;
        var username = context.Principal?.Identity?.Name;

        var isRevoked = (!string.IsNullOrWhiteSpace(sessionId) && revocationService.IsSessionRevoked(sessionId, issuedUtc)) ||
                        (!string.IsNullOrWhiteSpace(username) && revocationService.IsSessionRevoked(username, issuedUtc));

        if (isRevoked)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync("Cookies");
        }

        Assert.That(context.Principal, Is.Null);
        await authService.Received(1).SignOutAsync(httpContext, "Cookies", Arg.Any<AuthenticationProperties>());
    }

    [TestCase("/")]
    [TestCase("/settings")]
    [TestCase("/torrents/1")]
    [TestCase("/api/v1/queue")]
    [TestCase("/media/detail?id=42")]
    [TestCase("/app#section")]
    public void IsLocalUrl_ValidLocalPaths_ReturnsTrue(string url)
    {
        Assert.That(AuthController.IsLocalUrl(url), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/\\/evil.com")]
    [TestCase("/\\evil.com")]
    [TestCase("/\\/")]
    [TestCase("/\\")]
    [TestCase("/foo\\bar")]
    [TestCase("/%5cevil.com")]
    [TestCase("//evil.com")]
    [TestCase("//")]
    [TestCase("///")]
    [TestCase("///evil.com")]
    [TestCase("/@evil.com")]
    [TestCase("/@www.google.com")]
    [TestCase("http://evil.com")]
    [TestCase("https://evil.com")]
    [TestCase("ftp://evil.com")]
    [TestCase("javascript:alert(1)")]
    [TestCase("data:text/html,test")]
    [TestCase("evil.com/login")]
    [TestCase("settings")]
    [TestCase("/evil.com\0")]
    [TestCase("/evil.com\r\n")]
    [TestCase("/evil.com\t")]
    public void IsLocalUrl_MaliciousOrInvalidPaths_ReturnsFalse(string url)
    {
        Assert.That(AuthController.IsLocalUrl(url), Is.False);
    }

    [TestCase("/settings", null, "/settings")]
    [TestCase("/torrents/1", "", "/torrents/1")]
    [TestCase("/", null, "/")]
    [TestCase("http://evil.com", null, "/")]
    [TestCase("//evil.com", null, "/")]
    [TestCase("/\\/evil.com", null, "/")]
    [TestCase("/\\evil.com", null, "/")]
    [TestCase("/@evil.com", null, "/")]
    [TestCase(null, null, "/")]
    [TestCase("", null, "/")]
    public void SanitizeRedirectUrl_WithoutPathBase_ReturnsExpected(string url, string pathBase, string expected)
    {
        Assert.That(AuthController.SanitizeRedirectUrl(url, pathBase), Is.EqualTo(expected));
    }

    [TestCase("/settings", "/seedarr", "/seedarr/settings")]
    [TestCase("/torrents/1", "/seedarr", "/seedarr/torrents/1")]
    [TestCase("/", "/seedarr", "/seedarr/")]
    [TestCase("/seedarr/settings", "/seedarr", "/seedarr/settings")]
    [TestCase("/seedarr", "/seedarr", "/seedarr")]
    [TestCase("/seedarr/", "/seedarr", "/seedarr/")]
    [TestCase("/settings", "seedarr", "/seedarr/settings")]
    [TestCase("/settings", "/seedarr/", "/seedarr/settings")]
    [TestCase("http://evil.com", "/seedarr", "/seedarr/")]
    [TestCase("//evil.com", "/seedarr", "/seedarr/")]
    [TestCase("/\\/evil.com", "/seedarr", "/seedarr/")]
    [TestCase("/\\evil.com", "/seedarr", "/seedarr/")]
    [TestCase("/@evil.com", "/seedarr", "/seedarr/")]
    [TestCase(null, "/seedarr", "/seedarr/")]
    [TestCase("", "/seedarr", "/seedarr/")]
    public void SanitizeRedirectUrl_WithPathBase_ReturnsSubpathPrefixed(string url, string pathBase, string expected)
    {
        Assert.That(AuthController.SanitizeRedirectUrl(url, pathBase), Is.EqualTo(expected));
    }

    [TestCase(null, "")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase("/", "")]
    [TestCase("/seedarr", "/seedarr")]
    [TestCase("/seedarr/", "/seedarr")]
    [TestCase("seedarr", "/seedarr")]
    [TestCase("seedarr/", "/seedarr")]
    [TestCase("/app/seedarr", "/app/seedarr")]
    public void NormalizePathBase_NormalizesCorrectly(string input, string expected)
    {
        Assert.That(AuthController.NormalizePathBase(input), Is.EqualTo(expected));
    }

    [Test]
    public async Task Login_ConsecutiveFailedAttempts_TriggersHttp429TooManyRequestsWithRetryAfter()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new LoginRequestResource { Username = "admin", Password = "wrong-password" };

        for (var i = 0; i < 5; i++)
        {
            var failResult = await _controller.Login(request);
            Assert.That(failResult.Result, Is.TypeOf<UnauthorizedObjectResult>());
        }

        var blockedResult = await _controller.Login(request);
        Assert.That(blockedResult.Result, Is.TypeOf<ObjectResult>());

        var objResult = (ObjectResult)blockedResult.Result;
        Assert.That(objResult.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));

        Assert.That(httpContext.Response.Headers.ContainsKey("Retry-After"), Is.True);
        var retryAfterValue = int.Parse(httpContext.Response.Headers["Retry-After"].ToString());
        Assert.That(retryAfterValue, Is.GreaterThan(0));

        var error = objResult.Value.GetType().GetProperty("error")?.GetValue(objResult.Value) as string;
        Assert.That(error, Does.Contain("Too many failed login attempts"));
    }

    [Test]
    public async Task Login_SuccessfulLogin_ResetsFailureCounter()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");

        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var badRequest = new LoginRequestResource { Username = "admin", Password = "wrong-password" };
        var goodRequest = new LoginRequestResource { Username = "admin", Password = "master-api-key" };

        for (var i = 0; i < 4; i++)
        {
            var failResult = await _controller.Login(badRequest);
            Assert.That(failResult.Result, Is.TypeOf<UnauthorizedObjectResult>());
        }

        Assert.That(_loginRateLimiter.GetFailedAttempts("192.168.1.100"), Is.EqualTo(4));

        var successResult = await _controller.Login(goodRequest);
        Assert.That(successResult.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(_loginRateLimiter.GetFailedAttempts("192.168.1.100"), Is.EqualTo(0));

        for (var i = 0; i < 4; i++)
        {
            var failResult = await _controller.Login(badRequest);
            Assert.That(failResult.Result, Is.TypeOf<UnauthorizedObjectResult>());
        }

        Assert.That(_loginRateLimiter.GetFailedAttempts("192.168.1.100"), Is.EqualTo(4));
    }

    [Test]
    public async Task Login_DifferentClientIps_HaveIsolatedFailureBuckets()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var httpContextIp1 = new DefaultHttpContext();
        httpContextIp1.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContextIp1 };

        var badRequest = new LoginRequestResource { Username = "admin", Password = "wrong-password" };

        for (var i = 0; i < 5; i++)
        {
            var failResult = await _controller.Login(badRequest);
            Assert.That(failResult.Result, Is.TypeOf<UnauthorizedObjectResult>());
        }

        var blockedResult = await _controller.Login(badRequest);
        Assert.That(blockedResult.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)blockedResult.Result).StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));

        var httpContextIp2 = new DefaultHttpContext();
        httpContextIp2.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.200");
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContextIp2 };

        var ip2Result = await _controller.Login(badRequest);
        Assert.That(ip2Result.Result, Is.TypeOf<UnauthorizedObjectResult>());
        Assert.That(_loginRateLimiter.GetFailedAttempts("192.168.1.200"), Is.EqualTo(1));
        Assert.That(_loginRateLimiter.IsRateLimited("192.168.1.200", out _), Is.False);
        Assert.That(_loginRateLimiter.IsRateLimited("192.168.1.100", out _), Is.True);
    }

    [Test]
    public async Task Login_WithXForwardedForHeader_ExtractsClientIpAndIsolatesBucket()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("master-api-key");

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.50, 10.0.0.1";
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var badRequest = new LoginRequestResource { Username = "admin", Password = "wrong-password" };

        for (var i = 0; i < 5; i++)
        {
            var failResult = await _controller.Login(badRequest);
            Assert.That(failResult.Result, Is.TypeOf<UnauthorizedObjectResult>());
        }

        var blockedResult = await _controller.Login(badRequest);
        Assert.That(blockedResult.Result, Is.TypeOf<ObjectResult>());
        Assert.That(((ObjectResult)blockedResult.Result).StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));

        Assert.That(_loginRateLimiter.IsRateLimited("203.0.113.50", out _), Is.True);
        Assert.That(_loginRateLimiter.IsRateLimited("10.0.0.1", out _), Is.False);
    }

    [TestCase("192.168.1.1", "192.168.1.1")]
    [TestCase("192.168.1.1:8080", "192.168.1.1")]
    [TestCase("::1", "::1")]
    [TestCase("[2001:db8::1]:443", "2001:db8::1")]
    [TestCase("::ffff:192.0.2.1", "192.0.2.1")]
    [TestCase("not-an-ip", null)]
    [TestCase(null, null)]
    [TestCase("", null)]
    [TestCase("   ", null)]
    public void CleanAndValidateIp_ReturnsExpectedResult(string input, string expected)
    {
        Assert.That(AuthController.CleanAndValidateIp(input), Is.EqualTo(expected));
    }

    [Test]
    public void GetCurrentUser_WhenAuthenticationDisabled_ReturnsAdminUserWithAdminRole()
    {
        _configFileProvider.AuthenticationEnabled.Returns(false);
        var httpContext = new DefaultHttpContext();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = _controller.GetCurrentUser();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var user = okResult.Value as CurrentUserResource;
        Assert.That(user, Is.Not.Null);
        Assert.That(user.IsAuthenticated, Is.True);
        Assert.That(user.Roles, Contains.Item(Roles.Admin));
    }

    [Test]
    public void GetCurrentUser_WhenAuthenticatedWithoutRoles_DefaultsToReadOnlyRole()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim("Identifier", "testuser"),
        };
        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = _controller.GetCurrentUser();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var user = okResult.Value as CurrentUserResource;
        Assert.That(user, Is.Not.Null);
        Assert.That(user.IsAuthenticated, Is.True);
        Assert.That(user.Roles, Contains.Item(Roles.ReadOnly));
        Assert.That(user.Roles, Does.Not.Contain(Roles.Admin));
    }

    [Test]
    public void GetCurrentUser_WhenAuthenticatedWithRoles_PreservesAssignedRoles()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "adminuser"),
            new Claim(ClaimTypes.Role, Roles.Admin),
        };
        var identity = new ClaimsIdentity(claims, "Cookies");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = _controller.GetCurrentUser();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var user = okResult.Value as CurrentUserResource;
        Assert.That(user, Is.Not.Null);
        Assert.That(user.Roles, Contains.Item(Roles.Admin));
    }

    [Test]
    public async Task Login_WhenNonAdminCredentialsValid_AssignsReadOnlyRole()
    {
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _configFileProvider.ApiKey.Returns("shared-password");

        var httpContext = new DefaultHttpContext();
        var authService = Substitute.For<IAuthenticationService>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuthenticationService)).Returns(authService);
        httpContext.RequestServices = serviceProvider;
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        ClaimsPrincipal capturedPrincipal = null;
        await authService.SignInAsync(
            httpContext,
            "Cookies",
            Arg.Do<ClaimsPrincipal>(p => capturedPrincipal = p),
            Arg.Any<AuthenticationProperties>());

        var request = new LoginRequestResource
        {
            Username = "standarduser",
            Password = "shared-password",
        };

        var result = await _controller.Login(request);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var user = okResult.Value as CurrentUserResource;
        Assert.That(user, Is.Not.Null);
        Assert.That(user.Roles, Contains.Item(Roles.ReadOnly));
        Assert.That(user.Roles, Does.Not.Contain(Roles.Admin));
        Assert.That(capturedPrincipal.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == Roles.ReadOnly), Is.True);
    }
}
