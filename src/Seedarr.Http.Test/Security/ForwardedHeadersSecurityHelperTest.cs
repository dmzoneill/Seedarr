using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using NUnit.Framework;
using Seedarr.Http.Security;

namespace Seedarr.Http.Test.Security;

[TestFixture]
public class ForwardedHeadersSecurityHelperTest
{
    [Test]
    public void ApplyTrustedProxyConfiguration_WithNoConfiguredProxies_OnlyTrustsLoopback()
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSecurityHelper.ApplyTrustedProxyConfiguration(options, string.Empty);

        Assert.That(options.KnownProxies, Has.Count.EqualTo(2));
        Assert.That(options.KnownProxies, Does.Contain(IPAddress.Loopback));
        Assert.That(options.KnownProxies, Does.Contain(IPAddress.IPv6Loopback));
        Assert.That(options.KnownIPNetworks, Is.Empty);
    }

    [Test]
    public void ApplyTrustedProxyConfiguration_ParsesSingleIpAndCidr()
    {
        var options = new ForwardedHeadersOptions();
        ForwardedHeadersSecurityHelper.ApplyTrustedProxyConfiguration(options, "198.51.100.2, 10.0.0.0/8");

        Assert.That(options.KnownProxies, Does.Contain(IPAddress.Parse("198.51.100.2")));
        Assert.That(options.KnownIPNetworks, Has.Count.EqualTo(1));
    }
}
