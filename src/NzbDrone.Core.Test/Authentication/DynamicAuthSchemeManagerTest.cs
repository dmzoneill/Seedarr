using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using Seedarr.Http.Authentication;

namespace NzbDrone.Core.Test.Authentication;

[TestFixture]
public class DynamicAuthSchemeManagerTest
{
    [Test]
    public async Task RegisterOrUpdateOidcProviderAsync_configures_OnRedirectToIdentityProvider_to_enforce_https()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var manager = new DynamicAuthSchemeManager(sp, repo);

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test1",
            Name = "Test Provider",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://auth.example.com",
            ClientId = "client-id",
        };

        await manager.RegisterOrUpdateOidcProviderAsync(provider);

        var cache = sp.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        var options = cache.GetOrAdd("Oidc_test1", () => new OpenIdConnectOptions());

        Assert.That(options.Events.OnRedirectToIdentityProvider, Is.Not.Null);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Forwarded-Proto"] = "https";

        var scheme = new AuthenticationScheme("Oidc_test1", "Test Provider", typeof(OpenIdConnectHandler));
        var redirectContext = new RedirectContext(
            httpContext,
            scheme,
            options,
            new AuthenticationProperties())
        {
            ProtocolMessage = new OpenIdConnectMessage
            {
                RedirectUri = "http://seedarr.example.com/signin-oidc-test1",
            },
        };

        await options.Events.RedirectToIdentityProvider(redirectContext);

        Assert.That(redirectContext.ProtocolMessage.RedirectUri, Is.EqualTo("https://seedarr.example.com/signin-oidc-test1"));
    }
}
