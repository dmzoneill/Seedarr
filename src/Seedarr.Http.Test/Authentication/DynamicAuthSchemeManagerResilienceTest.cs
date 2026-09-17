using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using Seedarr.Http.Authentication;

namespace Seedarr.Http.Test.Authentication;

[TestFixture]
public class DynamicAuthSchemeManagerResilienceTest
{
    [Test]
    public async Task InitializeConfiguredProvidersAsync_should_handle_offline_idp_without_crashing_and_schedule_retry()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();

        var failingPostConfigure = Substitute.For<IPostConfigureOptions<OpenIdConnectOptions>>();
        failingPostConfigure.When(p => p.PostConfigure("Oidc_unreachable_idp", Arg.Any<OpenIdConnectOptions>()))
            .Do(_ => throw new InvalidOperationException("IDX20803: Unable to obtain configuration from: https://unreachable.example.com/.well-known/openid-configuration"));

        services.AddSingleton(failingPostConfigure);
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var offlineProvider = new IdentityProviderDefinition
        {
            ProviderId = "unreachable_idp",
            Name = "Unreachable Authentik",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://unreachable.example.com",
            ClientId = "seedarr-client",
            IsEnabled = true,
        };

        var onlineProvider = new IdentityProviderDefinition
        {
            ProviderId = "online_idp",
            Name = "Online Provider",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://online.example.com",
            ClientId = "seedarr-client",
            IsEnabled = true,
        };

        repo.GetEnabled().Returns(new[] { offlineProvider, onlineProvider });

        var manager = new DynamicAuthSchemeManager(sp, repo);

        Assert.DoesNotThrowAsync(async () => await manager.InitializeConfiguredProvidersAsync());

        // Provider configuration was not corrupted or disabled
        repo.DidNotReceive().Update(Arg.Is<IdentityProviderDefinition>(p => p.ProviderId == "unreachable_idp" && !p.IsEnabled));

        // Online provider was successfully registered despite offline provider error
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var onlineScheme = await schemeProvider.GetSchemeAsync("Oidc_online_idp");
        Assert.That(onlineScheme, Is.Not.Null);

        // Offline provider scheduled for retry
        Assert.That(manager.HasPendingRetry("unreachable_idp"), Is.True);
    }

    [Test]
    public async Task InitializeConfiguredProvidersAsync_should_defer_metadata_discovery_and_register_scheme()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var offlineProvider = new IdentityProviderDefinition
        {
            ProviderId = "offline_booting_idp",
            Name = "Booting Keycloak",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://keycloak.local/realms/seedarr",
            MetadataUrl = "https://keycloak.local/realms/seedarr/.well-known/openid-configuration",
            ClientId = "seedarr-app",
            IsEnabled = true,
        };

        repo.GetEnabled().Returns(new[] { offlineProvider });

        var manager = new DynamicAuthSchemeManager(sp, repo);

        await manager.InitializeConfiguredProvidersAsync();

        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync("Oidc_offline_booting_idp");
        Assert.That(scheme, Is.Not.Null);
        Assert.That(scheme.DisplayName, Is.EqualTo("Booting Keycloak"));

        var cache = sp.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        var options = cache.GetOrAdd("Oidc_offline_booting_idp", () => new OpenIdConnectOptions());
        Assert.That(options.MetadataAddress, Is.EqualTo("https://keycloak.local/realms/seedarr/.well-known/openid-configuration"));
        Assert.That(offlineProvider.IsEnabled, Is.True);
    }
}
