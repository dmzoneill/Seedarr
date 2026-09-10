using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using Seedarr.Api.V1.Config;
using Seedarr.Http.Authentication;

namespace NzbDrone.Core.Test.Config;

[TestFixture]
public class IdentityProviderConfigControllerTest
{
    private IIdentityProviderService _providerService;
    private IDynamicAuthSchemeManager _dynamicAuthManager;
    private IdentityProviderConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _providerService = Substitute.For<IIdentityProviderService>();
        _dynamicAuthManager = Substitute.For<IDynamicAuthSchemeManager>();
        _controller = new IdentityProviderConfigController(_providerService, _dynamicAuthManager);
    }

    [Test]
    public void GetAll_ReturnsMappedResourcesWithMaskedSecrets()
    {
        var providers = new List<IdentityProviderDefinition>
        {
            new()
            {
                Id = 1,
                ProviderId = "entra",
                Name = "Microsoft Entra ID",
                ProviderType = IdentityProviderType.Oidc,
                IsEnabled = true,
                ClientId = "entra-client-id",
                ClientSecretEncrypted = "real-secret-123",
            },
            new()
            {
                Id = 2,
                ProviderId = "auth0",
                Name = "Auth0",
                ProviderType = IdentityProviderType.Oidc,
                IsEnabled = false,
                ClientId = "auth0-client-id",
                ClientSecretEncrypted = null,
            },
        };

        _providerService.GetAll().Returns(providers);

        var result = _controller.GetAll();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        var list = ok.Value as List<IdentityProviderResource>;
        Assert.That(list, Is.Not.Null);
        Assert.That(list.Count, Is.EqualTo(2));

        Assert.That(list[0].ProviderId, Is.EqualTo("entra"));
        Assert.That(list[0].ClientSecret, Is.EqualTo("********"));

        Assert.That(list[1].ProviderId, Is.EqualTo("auth0"));
        Assert.That(list[1].ClientSecret, Is.Null);
    }

    [Test]
    public void GetById_WhenExists_ReturnsResourceWithMaskedSecret()
    {
        var provider = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "keycloak",
            Name = "Keycloak",
            ClientSecretEncrypted = "secret-with*asterisk",
        };

        _providerService.GetById(1).Returns(provider);

        var result = _controller.GetById(1);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var ok = (OkObjectResult)result.Result;
        var res = ok.Value as IdentityProviderResource;
        Assert.That(res, Is.Not.Null);
        Assert.That(res.ClientSecret, Is.EqualTo("********"));
    }

    [Test]
    public void GetById_WhenNotFound_ReturnsNotFound()
    {
        _providerService.GetById(99).Returns((IdentityProviderDefinition)null);

        var result = _controller.GetById(99);

        Assert.That(result.Result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public void Create_WhenValid_AddsProviderAndRegistersScheme()
    {
        var resource = new IdentityProviderResource
        {
            ProviderId = "okta",
            Name = "Okta SSO",
            IsEnabled = true,
            ClientId = "okta-client",
            ClientSecret = "secret*with*asterisks!123",
        };

        _providerService.Add(Arg.Any<IdentityProviderDefinition>()).Returns(callInfo =>
        {
            var def = callInfo.Arg<IdentityProviderDefinition>();
            def.Id = 42;
            return def;
        });

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.TypeOf<CreatedResult>());
        var created = (CreatedResult)result.Result;
        Assert.That(created.Location, Is.EqualTo("/api/v1/config/auth/providers/42"));

        _providerService.Received(1).Add(Arg.Is<IdentityProviderDefinition>(p =>
            p.ProviderId == "okta" &&
            p.ClientSecretEncrypted == "secret*with*asterisks!123"));

        _ = _dynamicAuthManager.Received(1).RegisterOrUpdateOidcProviderAsync(Arg.Is<IdentityProviderDefinition>(p => p.Id == 42));
    }

    [Test]
    public void Create_WhenNull_ReturnsBadRequest()
    {
        var result = _controller.Create(null);

        Assert.That(result.Result, Is.TypeOf<BadRequestResult>());
    }

    [Test]
    public void Update_WhenNull_ReturnsBadRequest()
    {
        var result = _controller.Update(1, null);

        Assert.That(result.Result, Is.TypeOf<BadRequestResult>());
    }

    [Test]
    public void Update_WhenNotFound_ReturnsNotFound()
    {
        _providerService.GetById(99).Returns((IdentityProviderDefinition)null);

        var result = _controller.Update(99, new IdentityProviderResource { ProviderId = "test", Name = "Test" });

        Assert.That(result.Result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public void Update_WhenSecretIsMaskedWithEightAsterisks_PreservesExistingSecret()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "authentik",
            Name = "Authentik",
            ClientSecretEncrypted = "existing-real-secret",
            IsEnabled = true,
        };

        _providerService.GetById(1).Returns(existing);
        _providerService.Update(Arg.Any<IdentityProviderDefinition>()).Returns(x => x.Arg<IdentityProviderDefinition>());

        var resource = new IdentityProviderResource
        {
            ProviderId = "authentik",
            Name = "Authentik Updated",
            IsEnabled = true,
            ClientSecret = "********",
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        _providerService.Received(1).Update(Arg.Is<IdentityProviderDefinition>(p =>
            p.Id == 1 &&
            p.ClientSecretEncrypted == "existing-real-secret" &&
            p.Name == "Authentik Updated"));
    }

    [Test]
    public void Update_WhenDisabled_RemovesProviderScheme()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test-provider",
            Name = "Test Provider",
            IsEnabled = true,
        };

        _providerService.GetById(1).Returns(existing);
        _providerService.Update(Arg.Any<IdentityProviderDefinition>()).Returns(x => x.Arg<IdentityProviderDefinition>());

        var resource = new IdentityProviderResource
        {
            ProviderId = "test-provider",
            Name = "Test Provider",
            IsEnabled = false,
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        _ = _dynamicAuthManager.Received(1).RemoveProviderSchemeAsync("test-provider");
    }

    [Test]
    public void Delete_WhenFound_DeletesProviderAndRemovesScheme()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 1,
            ProviderId = "test-provider",
            Name = "Test Provider",
        };

        _providerService.GetById(1).Returns(existing);

        var result = _controller.Delete(1);

        Assert.That(result, Is.TypeOf<NoContentResult>());
        _providerService.Received(1).Delete(1);
        _ = _dynamicAuthManager.Received(1).RemoveProviderSchemeAsync("test-provider");
    }

    [Test]
    public void Delete_WhenNotFound_ReturnsNotFound()
    {
        _providerService.GetById(99).Returns((IdentityProviderDefinition)null);

        var result = _controller.Delete(99);

        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task TestConnection_WhenNull_ReturnsBadRequest()
    {
        var result = await _controller.TestConnection(null);

        Assert.That(result, Is.TypeOf<BadRequestResult>());
    }

    [Test]
    public async Task TestConnection_WhenSecretIsMaskedAndExistingProviderFound_PreservesExistingSecret()
    {
        var existing = new IdentityProviderDefinition
        {
            Id = 5,
            ProviderId = "oidc-test",
            Name = "OIDC Test",
            ClientSecretEncrypted = "real-underlying-secret",
        };

        _providerService.GetById(5).Returns(existing);
        _providerService.TestConnectionAsync(Arg.Any<IdentityProviderDefinition>()).Returns(Task.FromResult(true));

        var resource = new IdentityProviderResource
        {
            Id = 5,
            ProviderId = "oidc-test",
            Name = "OIDC Test",
            ClientSecret = "********",
        };

        var result = await _controller.TestConnection(resource);

        Assert.That(result, Is.TypeOf<OkObjectResult>());
        await _providerService.Received(1).TestConnectionAsync(Arg.Is<IdentityProviderDefinition>(p =>
            p.ClientSecretEncrypted == "real-underlying-secret"));
    }

    [Test]
    public void Create_WhenValidationFails_ReturnsBadRequest()
    {
        var resource = new IdentityProviderResource
        {
            ProviderId = "", // Invalid
            Name = "", // Invalid
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        _providerService.DidNotReceive().Add(Arg.Any<IdentityProviderDefinition>());
    }

    [Test]
    public void Update_WhenValidationFails_ReturnsBadRequest()
    {
        var resource = new IdentityProviderResource
        {
            ProviderId = "", // Invalid
            Name = "", // Invalid
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        _providerService.DidNotReceive().Update(Arg.Any<IdentityProviderDefinition>());
    }
}
