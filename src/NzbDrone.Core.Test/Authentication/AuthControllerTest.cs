using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Auth;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class AuthControllerTest
{
    private IIdentityProviderService _identityProviderService;
    private IConfigFileProvider _configFileProvider;
    private AuthController _controller;

    [SetUp]
    public void SetUp()
    {
        _identityProviderService = Substitute.For<IIdentityProviderService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _controller = new AuthController(_identityProviderService, _configFileProvider);
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
}
