using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

    [Test]
    public void ResolveRoles_should_fallback_to_User_when_no_rules_configured()
    {
        var claims = new List<Claim>
        {
            new("groups", "admin"),
            new("role", "administrator"),
        };

        var rolesNull = DynamicAuthSchemeManager.ResolveRoles(claims, null);
        var rolesEmpty = DynamicAuthSchemeManager.ResolveRoles(claims, "");
        var rolesWhitespace = DynamicAuthSchemeManager.ResolveRoles(claims, "   ");

        Assert.That(rolesNull, Is.EquivalentTo(new[] { "User" }));
        Assert.That(rolesEmpty, Is.EquivalentTo(new[] { "User" }));
        Assert.That(rolesWhitespace, Is.EquivalentTo(new[] { "User" }));
    }

    [Test]
    public void ResolveRoles_should_fallback_to_User_when_rules_do_not_match_claims()
    {
        var claims = new List<Claim>
        {
            new("groups", "viewers"),
            new("role", "guest"),
        };

        var rules = "{\"Admin\":\"^(admin|devops)$\",\"Operator\":\"^operators$\"}";
        var roles = DynamicAuthSchemeManager.ResolveRoles(claims, rules);

        Assert.That(roles, Is.EquivalentTo(new[] { "User" }));
    }

    [Test]
    public void ResolveRoles_should_assign_Admin_when_group_claim_matches_regex()
    {
        var claims = new List<Claim>
        {
            new("groups", "devops"),
        };

        var rules = "{\"Admin\":\"^(admin|devops)$\",\"Operator\":\"^operators$\"}";
        var roles = DynamicAuthSchemeManager.ResolveRoles(claims, rules);

        Assert.That(roles, Is.EquivalentTo(new[] { "Admin" }));
    }

    [Test]
    public void ResolveRoles_should_assign_Operator_when_roles_claim_matches_regex()
    {
        var claims = new List<Claim>
        {
            new("roles", "media-managers"),
        };

        var rules = "{\"Admin\":\"^(admin)$\",\"Operator\":\"^(operators|media-managers)$\"}";
        var roles = DynamicAuthSchemeManager.ResolveRoles(claims, rules);

        Assert.That(roles, Is.EquivalentTo(new[] { "Operator" }));
    }

    [Test]
    public void ResolveRoles_should_assign_multiple_roles_when_multiple_rules_match()
    {
        var claims = new List<Claim>
        {
            new("groups", "admin"),
            new("groups", "operators"),
        };

        var rules = "{\"Admin\":\"^admin$\",\"Operator\":\"^operators$\"}";
        var roles = DynamicAuthSchemeManager.ResolveRoles(claims, rules);

        Assert.That(roles, Is.EquivalentTo(new[] { "Admin", "Operator" }));
    }

    [Test]
    public void ResolveRoles_should_parse_json_array_claims()
    {
        var claims = new List<Claim>
        {
            new("cognito:groups", "[\"infrastructure\", \"users\"]"),
        };

        var rules = "{\"Admin\":\"^(admin|infrastructure)$\"}";
        var roles = DynamicAuthSchemeManager.ResolveRoles(claims, rules);

        Assert.That(roles, Is.EquivalentTo(new[] { "Admin" }));
    }

    [Test]
    public void ResolveRoles_should_parse_keycloak_realm_access_roles()
    {
        var claims = new List<Claim>
        {
            new("realm_access", "{\"roles\":[\"realm-admin\",\"default-roles-seedarr\"]}"),
        };

        var rules = "{\"Admin\":\"^realm-admin$\"}";
        var roles = DynamicAuthSchemeManager.ResolveRoles(claims, rules);

        Assert.That(roles, Is.EquivalentTo(new[] { "Admin" }));
    }

    [Test]
    public void ResolveRoles_should_parse_comma_separated_claims()
    {
        var claims = new List<Claim>
        {
            new("groups", "staff, devops, contributors"),
        };

        var rules = "{\"Admin\":\"^devops$\"}";
        var roles = DynamicAuthSchemeManager.ResolveRoles(claims, rules);

        Assert.That(roles, Is.EquivalentTo(new[] { "Admin" }));
    }

    [Test]
    public void ResolveRoles_should_handle_invalid_json_rules_gracefully_and_return_User()
    {
        var claims = new List<Claim>
        {
            new("groups", "admin"),
        };

        var invalidRules = "this is not valid json";
        var roles = DynamicAuthSchemeManager.ResolveRoles(claims, invalidRules);

        Assert.That(roles, Is.EquivalentTo(new[] { "User" }));
    }

    [Test]
    public async Task RegisterOrUpdateOidcProviderAsync_OnTokenValidated_should_assign_mapped_roles_and_not_hardcoded_admin()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var manager = new DynamicAuthSchemeManager(sp, repo);

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test_roles",
            Name = "Test Role Provider",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://auth.example.com",
            ClientId = "client-id",
            RoleMappingRules = "{\"Admin\":\"^seedarr-admin$\"}",
        };

        await manager.RegisterOrUpdateOidcProviderAsync(provider);

        var cache = sp.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        var options = cache.GetOrAdd("Oidc_test_roles", () => new OpenIdConnectOptions());

        Assert.That(options.Events.OnTokenValidated, Is.Not.Null);

        // Case 1: Non-admin user gets "User" role instead of escalating to "Admin"
        var httpContext1 = new DefaultHttpContext();
        var scheme1 = new AuthenticationScheme("Oidc_test_roles", "Test Role Provider", typeof(OpenIdConnectHandler));
        var userClaims1 = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-123"),
                new Claim("preferred_username", "alice"),
                new Claim("groups", "regular-users"),
            },
            "TestAuth");

        var tokenContext1 = new TokenValidatedContext(
            httpContext1,
            scheme1,
            options,
            new ClaimsPrincipal(userClaims1),
            new AuthenticationProperties());

        await options.Events.TokenValidated(tokenContext1);

        var resultingRoles1 = tokenContext1.Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.That(resultingRoles1, Is.EquivalentTo(new[] { "User" }));
        Assert.That(resultingRoles1, Does.Not.Contain("Admin"));

        // Case 2: Matching admin group gets "Admin" role
        var httpContext2 = new DefaultHttpContext();
        var userClaims2 = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "admin-456"),
                new Claim("preferred_username", "bob"),
                new Claim("groups", "seedarr-admin"),
            },
            "TestAuth");

        var tokenContext2 = new TokenValidatedContext(
            httpContext2,
            scheme1,
            options,
            new ClaimsPrincipal(userClaims2),
            new AuthenticationProperties());

        await options.Events.TokenValidated(tokenContext2);

        var resultingRoles2 = tokenContext2.Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.That(resultingRoles2, Is.EquivalentTo(new[] { "Admin" }));
    }

    [Test]
    public async Task InitializeConfiguredProvidersAsync_should_handle_offline_idp_discovery_without_crashing_and_schedule_retry()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();

        var failingPostConfigure = Substitute.For<IPostConfigureOptions<OpenIdConnectOptions>>();
        failingPostConfigure.When(p => p.PostConfigure("Oidc_offline_idp", Arg.Any<OpenIdConnectOptions>()))
            .Do(_ => throw new InvalidOperationException("IDX20803: Unable to obtain configuration from: https://offline.example.com/.well-known/openid-configuration"));

        services.AddSingleton(failingPostConfigure);
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var offlineProvider = new IdentityProviderDefinition
        {
            ProviderId = "offline_idp",
            Name = "Offline Keycloak",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://offline.example.com",
            ClientId = "client-id",
            IsEnabled = true,
        };

        var onlineProvider = new IdentityProviderDefinition
        {
            ProviderId = "online_idp",
            Name = "Online Provider",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://online.example.com",
            ClientId = "client-id",
            IsEnabled = true,
        };

        repo.GetEnabled().Returns(new[] { offlineProvider, onlineProvider });

        var manager = new DynamicAuthSchemeManager(sp, repo);

        // Act - should not throw
        Assert.DoesNotThrowAsync(async () => await manager.InitializeConfiguredProvidersAsync());

        // Verify provider was not deactivated or corrupted in the repository
        repo.DidNotReceive().Update(Arg.Is<IdentityProviderDefinition>(p => p.ProviderId == "offline_idp" && !p.IsEnabled));

        // Verify online provider was registered
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var onlineScheme = await schemeProvider.GetSchemeAsync("Oidc_online_idp");
        Assert.That(onlineScheme, Is.Not.Null);
    }

    [Test]
    public async Task InitializeConfiguredProvidersAsync_should_defer_metadata_discovery_and_register_scheme_when_idp_offline()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var offlineProvider = new IdentityProviderDefinition
        {
            ProviderId = "slow_boot_authentik",
            Name = "Slow Boot Authentik",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://authentik.local/application/o/seedarr/",
            MetadataUrl = "https://authentik.local/application/o/seedarr/.well-known/openid-configuration",
            ClientId = "seedarr-client",
            IsEnabled = true,
        };

        repo.GetEnabled().Returns(new[] { offlineProvider });

        var manager = new DynamicAuthSchemeManager(sp, repo);

        await manager.InitializeConfiguredProvidersAsync();

        // Scheme should be registered with deferred discovery
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync("Oidc_slow_boot_authentik");
        Assert.That(scheme, Is.Not.Null);
        Assert.That(scheme.DisplayName, Is.EqualTo("Slow Boot Authentik"));

        // Options should preserve MetadataAddress
        var cache = sp.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        var options = cache.GetOrAdd("Oidc_slow_boot_authentik", () => new OpenIdConnectOptions());
        Assert.That(options.MetadataAddress, Is.EqualTo("https://authentik.local/application/o/seedarr/.well-known/openid-configuration"));

        // Provider configuration was not corrupted
        Assert.That(offlineProvider.IsEnabled, Is.True);
    }

    [Test]
    public async Task RemoveProviderSchemeAsync_should_clear_pending_retry()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var provider = new IdentityProviderDefinition
        {
            ProviderId = "transient_idp",
            Name = "Transient Provider",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://transient.example.com",
            ClientId = "client-id",
            IsEnabled = true,
        };

        var manager = new DynamicAuthSchemeManager(sp, repo);
        manager.ScheduleRetry(provider, 60);

        Assert.That(manager.HasPendingRetry("transient_idp"), Is.True);

        await manager.RemoveProviderSchemeAsync("transient_idp");

        Assert.That(manager.HasPendingRetry("transient_idp"), Is.False);
    }

    [Test]
    public async Task RegisterOrUpdateOidcProviderAsync_should_update_existing_scheme_and_refresh_options()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var manager = new DynamicAuthSchemeManager(sp, repo);

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test_update",
            Name = "Initial Name",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://auth.example.com",
            ClientId = "client-id-1",
            IsEnabled = true,
        };

        await manager.RegisterOrUpdateOidcProviderAsync(provider);

        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var initialScheme = await schemeProvider.GetSchemeAsync("Oidc_test_update");
        Assert.That(initialScheme, Is.Not.Null);
        Assert.That(initialScheme.DisplayName, Is.EqualTo("Initial Name"));

        var cache = sp.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        var initialOptions = cache.GetOrAdd("Oidc_test_update", () => new OpenIdConnectOptions());
        Assert.That(initialOptions.ClientId, Is.EqualTo("client-id-1"));

        provider.Name = "Updated Name";
        provider.ClientId = "client-id-2";

        await manager.RegisterOrUpdateOidcProviderAsync(provider);

        var updatedScheme = await schemeProvider.GetSchemeAsync("Oidc_test_update");
        Assert.That(updatedScheme, Is.Not.Null);
        Assert.That(updatedScheme.DisplayName, Is.EqualTo("Updated Name"));

        var updatedOptions = cache.GetOrAdd("Oidc_test_update", () => new OpenIdConnectOptions());
        Assert.That(updatedOptions.ClientId, Is.EqualTo("client-id-2"));
    }

    [TestCase(null, "client-id")]
    [TestCase("", "client-id")]
    [TestCase("   ", "client-id")]
    [TestCase("https://auth.example.com", null)]
    [TestCase("https://auth.example.com", "")]
    [TestCase("https://auth.example.com", "   ")]
    public void RegisterOrUpdateOidcProviderAsync_should_throw_ArgumentException_when_required_fields_missing(string issuerUrl, string clientId)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var manager = new DynamicAuthSchemeManager(sp, repo);

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test_invalid",
            Name = "Invalid Provider",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = issuerUrl,
            ClientId = clientId,
            IsEnabled = true,
        };

        Assert.ThrowsAsync<ArgumentException>(async () => await manager.RegisterOrUpdateOidcProviderAsync(provider));
    }

    [Test]
    public async Task RemoveProviderSchemeAsync_should_remove_scheme_and_clear_options()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddAuthentication();
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IIdentityProviderRepository>();
        var manager = new DynamicAuthSchemeManager(sp, repo);

        var provider = new IdentityProviderDefinition
        {
            ProviderId = "test_remove",
            Name = "Provider To Remove",
            ProviderType = IdentityProviderType.Oidc,
            IssuerUrl = "https://auth.example.com",
            ClientId = "client-id",
            IsEnabled = true,
        };

        await manager.RegisterOrUpdateOidcProviderAsync(provider);

        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.That(await schemeProvider.GetSchemeAsync("Oidc_test_remove"), Is.Not.Null);

        await manager.RemoveProviderSchemeAsync("test_remove");

        Assert.That(await schemeProvider.GetSchemeAsync("Oidc_test_remove"), Is.Null);
    }
}
