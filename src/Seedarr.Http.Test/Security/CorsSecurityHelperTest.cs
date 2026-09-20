// Copyright (c) PlaceholderCompany. All rights reserved.

using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Security;

namespace Seedarr.Http.Test.Security;

[TestFixture]
public class CorsSecurityHelperTest
{
    [TestCase((string)null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("	")]
    public void IsOriginAllowed_WhenOriginIsNullOrWhiteSpace_ReturnsFalse(string origin)
    {
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, (string)null), Is.False);
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, "https://example.com"), Is.False);
    }

    [TestCase("not-a-valid-uri")]
    [TestCase("://invalid")]
    [TestCase("javascript:alert(1)")]
    public void IsOriginAllowed_WhenOriginIsInvalidUri_ReturnsFalse(string origin)
    {
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, (string)null), Is.False);
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, "https://example.com"), Is.False);
    }

    [TestCase("http://localhost")]
    [TestCase("http://localhost:8989")]
    [TestCase("https://localhost:9898")]
    [TestCase("http://127.0.0.1")]
    [TestCase("http://127.0.0.1:8080")]
    [TestCase("http://127.0.0.2:9898")]
    [TestCase("http://[::1]")]
    [TestCase("http://[::1]:9898")]
    public void IsOriginAllowed_WhenOriginIsLocalhostOrLoopback_ReturnsTrue(string origin)
    {
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, (string)null), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, string.Empty), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, "https://other.com"), Is.True);
    }

    [TestCase("https://evil.com")]
    [TestCase("http://attacker.local")]
    [TestCase("https://malicious.org:8080")]
    public void IsOriginAllowed_WhenOriginIsUntrustedAndNoAllowedOriginsConfigured_ReturnsFalse(string origin)
    {
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, (string)null), Is.False);
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, string.Empty), Is.False);
        Assert.That(CorsSecurityHelper.IsOriginAllowed(origin, "   "), Is.False);
    }

    [Test]
    public void IsOriginAllowed_WhenOriginMatchesConfiguredAllowedOrigins_ReturnsTrue()
    {
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://app.example.com", "https://app.example.com"), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://app.example.com", "https://app.example.com/"), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://app.example.com/", "https://app.example.com"), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://app.example.com:8443", "https://app.example.com:8443"), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://app.example.com", "app.example.com"), Is.True);
        Assert.That(
            CorsSecurityHelper.IsOriginAllowed(
                "https://other.com",
                "https://app1.com, https://app2.com; https://other.com"),
            Is.True);
    }

    [Test]
    public void IsOriginAllowed_WhenOriginDoesNotMatchConfiguredAllowedOrigins_ReturnsFalse()
    {
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://evil.com", "https://good.com"), Is.False);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://app.example.com:9000", "https://app.example.com:8080"), Is.False);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("http://app.example.com", "https://app.example.com"), Is.False);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://notexample.com", "https://example.com"), Is.False);
    }

    [Test]
    public void IsOriginAllowed_WithConfigFileProvider_UsesConfiguredAllowedOrigins()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.AllowedOrigins.Returns("https://dashboard.example.com, https://portal.example.com");

        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://dashboard.example.com", config), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://portal.example.com", config), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("http://localhost:3000", config), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://evil.com", config), Is.False);
    }

    [Test]
    public void IsOriginAllowed_WithNullConfigFileProvider_AllowsLoopbackAndRejectsRemote()
    {
        Assert.That(CorsSecurityHelper.IsOriginAllowed("http://localhost:8080", (IConfigFileProvider)null), Is.True);
        Assert.That(CorsSecurityHelper.IsOriginAllowed("https://remote.com", (IConfigFileProvider)null), Is.False);
    }
}
