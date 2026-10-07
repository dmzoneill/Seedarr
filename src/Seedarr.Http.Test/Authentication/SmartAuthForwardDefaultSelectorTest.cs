// Copyright (c) FeedItOut. All rights reserved.

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Authentication;

namespace Seedarr.Http.Test.Authentication;

[TestFixture]
public class SmartAuthForwardDefaultSelectorTest
{
    private IConfigFileProvider _configFileProvider;
    private IIdentityProviderRepository _identityProviderRepository;
    private ServiceProvider _serviceProvider;

    [SetUp]
    public void SetUp()
    {
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.AuthenticationEnabled.Returns(true);
        _identityProviderRepository = Substitute.For<IIdentityProviderRepository>();

        var services = new ServiceCollection();
        services.AddSingleton(_configFileProvider);
        services.AddSingleton(_identityProviderRepository);
        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
    }

    private HttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.RequestServices = _serviceProvider;
        context.Request.Path = "/api/v1/system/status";
        return context;
    }

    [Test]
    public void SelectScheme_WhenSessionCookieAndInvalidApiKeyHeaderPresent_PrefersCookieScheme()
    {
        _configFileProvider.ApiKey.Returns("configured-secret-key");

        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "wrong-key";
        context.Request.Cookies.Append(SmartAuthForwardDefaultSelector.SessionCookieName, "session-value");

        var scheme = SmartAuthForwardDefaultSelector.SelectScheme(context);

        Assert.That(scheme, Is.EqualTo(SmartAuthForwardDefaultSelector.CookieScheme));
    }

    [Test]
    public void SelectScheme_WhenValidApiKeyHeaderPresent_SelectsApiKeyScheme()
    {
        _configFileProvider.ApiKey.Returns("configured-secret-key");

        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "configured-secret-key";

        var scheme = SmartAuthForwardDefaultSelector.SelectScheme(context);

        Assert.That(scheme, Is.EqualTo(ApiKeyAuthenticationOptions.DefaultScheme));
    }

    [Test]
    public void SelectScheme_WhenSessionCookieAndForwardAuthHeadersPresent_PrefersCookieScheme()
    {
        var forwardAuthIdp = new IdentityProviderDefinition
        {
            ProviderType = IdentityProviderType.ForwardAuth,
            IsEnabled = true,
            TrustedProxies = "10.0.0.2",
        };
        _identityProviderRepository.GetEnabled().Returns(new[] { forwardAuthIdp });

        var context = CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
        context.Request.Headers["X-Forwarded-User"] = "proxy-user";
        context.Request.Cookies.Append(SmartAuthForwardDefaultSelector.SessionCookieName, "session-value");

        var scheme = SmartAuthForwardDefaultSelector.SelectScheme(context);

        Assert.That(scheme, Is.EqualTo(SmartAuthForwardDefaultSelector.CookieScheme));
    }

    [Test]
    public void SelectScheme_WhenForwardAuthHeadersOnly_SelectsForwardAuthScheme()
    {
        var forwardAuthIdp = new IdentityProviderDefinition
        {
            ProviderType = IdentityProviderType.ForwardAuth,
            IsEnabled = true,
            TrustedProxies = "10.0.0.2",
        };
        _identityProviderRepository.GetEnabled().Returns(new[] { forwardAuthIdp });

        var context = CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
        context.Request.Headers["Remote-User"] = "proxy-user";

        var scheme = SmartAuthForwardDefaultSelector.SelectScheme(context);

        Assert.That(scheme, Is.EqualTo(ForwardAuthOptions.DefaultScheme));
    }

    [Test]
    public void SelectScheme_WhenLoopbackForwardAuthHeadersWithoutTrustedProxies_DoesNotSelectForwardAuthScheme()
    {
        var forwardAuthIdp = new IdentityProviderDefinition
        {
            ProviderType = IdentityProviderType.ForwardAuth,
            IsEnabled = true,
            TrustedProxies = string.Empty,
        };
        _identityProviderRepository.GetEnabled().Returns(new[] { forwardAuthIdp });

        var context = CreateContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["Remote-User"] = "attacker";
        context.Request.Headers["Remote-Groups"] = "admin";

        var scheme = SmartAuthForwardDefaultSelector.SelectScheme(context);

        Assert.That(scheme, Is.EqualTo(ApiKeyAuthenticationOptions.DefaultScheme));
    }
}
