// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Net;
using NUnit.Framework;
using Seedarr.Http.Security;

namespace Seedarr.Http.Test.Security;

[TestFixture]
public class IpSecurityHelperTest
{
    [Test]
    public void IsTrustedProxy_WhenRemoteIpIsNull_ReturnsFalse()
    {
        Assert.That(IpSecurityHelper.IsTrustedProxy(null), Is.False);
        Assert.That(IpSecurityHelper.IsTrustedProxy(null, "127.0.0.1, 10.0.0.0/8"), Is.False);
    }

    [TestCase("127.0.0.1")]
    [TestCase("127.0.0.2")]
    [TestCase("::1")]
    [TestCase("::ffff:127.0.0.1")]
    public void IsTrustedProxy_WhenLoopbackWithoutConfiguredProxies_ReturnsTrue(string ipString)
    {
        var ip = IPAddress.Parse(ipString);
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip), Is.True);
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip, string.Empty), Is.True);
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip, (string)null), Is.True);
    }

    [TestCase("192.168.1.1")]
    [TestCase("10.0.0.1")]
    [TestCase("8.8.8.8")]
    [TestCase("2001:db8::1")]
    public void IsTrustedProxy_WhenNonLoopbackWithoutConfiguredProxies_ReturnsFalse(string ipString)
    {
        var ip = IPAddress.Parse(ipString);
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip), Is.False);
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip, string.Empty), Is.False);
    }

    [Test]
    public void IsTrustedProxy_WhenSingleIpMatches_ReturnsTrue()
    {
        var ip = IPAddress.Parse("10.0.0.2");
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip, "10.0.0.2"), Is.True);
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip, "192.168.1.1, 10.0.0.2, 172.16.0.1"), Is.True);
    }

    [Test]
    public void IsTrustedProxy_WhenSingleIpDoesNotMatch_ReturnsFalse()
    {
        var ip = IPAddress.Parse("10.0.0.3");
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip, "10.0.0.2, 192.168.1.1"), Is.False);
    }

    [TestCase("10.0.0.1", "10.0.0.0/8", true)]
    [TestCase("10.254.254.254", "10.0.0.0/8", true)]
    [TestCase("11.0.0.1", "10.0.0.0/8", false)]
    [TestCase("172.16.0.1", "172.16.0.0/12", true)]
    [TestCase("172.31.255.254", "172.16.0.0/12", true)]
    [TestCase("172.32.0.1", "172.16.0.0/12", false)]
    [TestCase("192.168.1.50", "192.168.0.0/16", true)]
    [TestCase("192.169.1.50", "192.168.0.0/16", false)]
    public void IsTrustedProxy_WhenCidrRangeChecked_ReturnsExpected(string ipString, string cidr, bool expected)
    {
        var ip = IPAddress.Parse(ipString);
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip, cidr), Is.EqualTo(expected));
    }

    [Test]
    public void IsTrustedProxy_WhenIPv4MappedIPv6InCidr_ReturnsTrue()
    {
        var ip = IPAddress.Parse("::ffff:10.1.2.3");
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip, "10.0.0.0/8"), Is.True);
    }

    [Test]
    public void IsTrustedProxy_WithMultipleDelimitersAndMalformedEntries_DoesNotThrowAndMatchesValid()
    {
        var ip = IPAddress.Parse("10.5.5.5");
        var config = "invalid-cidr; 192.168.1.1,  not_an_ip  10.0.0.0/8\n\t::1";
        Assert.That(IpSecurityHelper.IsTrustedProxy(ip, config), Is.True);

        var untrustedIp = IPAddress.Parse("203.0.113.1");
        Assert.That(IpSecurityHelper.IsTrustedProxy(untrustedIp, config), Is.False);
    }
}
