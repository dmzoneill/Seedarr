using System.Collections.Generic;
using System.Threading.Tasks;
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
}
