using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Network;
namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class IPAddressExtensionsTest
{
    [TestCase("10.0.0.1", true)]
    [TestCase("10.255.255.254", true)]
    [TestCase("172.16.0.1", true)]
    [TestCase("172.31.255.254", true)]
    [TestCase("172.15.255.255", false)]
    [TestCase("172.32.0.1", false)]
    [TestCase("192.168.0.1", true)]
    [TestCase("192.168.1.100", true)]
    [TestCase("192.168.255.254", true)]
    [TestCase("192.167.1.1", false)]
    [TestCase("192.169.1.1", false)]
    [TestCase("169.254.1.1", true)]
    [TestCase("169.254.254.254", true)]
    [TestCase("8.8.8.8", false)]
    [TestCase("1.1.1.1", false)]
    [TestCase("93.184.216.34", false)]
    [TestCase("127.0.0.1", false)]
    public void IsLocalSubnet_IPv4_evaluates_correctly(string ipString, bool expected)
    {
        var ip = IPAddress.Parse(ipString);

        Assert.That(ip.IsLocalSubnet(), Is.EqualTo(expected));
        Assert.That(IPAddressExtensions.IsLocalSubnet(ipString), Is.EqualTo(expected));
    }

    [TestCase("fc00::1", true)]
    [TestCase("fd12:3456:789a::1", true)]
    [TestCase("fe80::1", true)]
    [TestCase("fe80::200:5aee:feaa:20a2", true)]
    [TestCase("2001:4860:4860::8888", false)]
    [TestCase("2606:4700:4700::1111", false)]
    [TestCase("::1", false)]
    public void IsLocalSubnet_IPv6_evaluates_correctly(string ipString, bool expected)
    {
        var ip = IPAddress.Parse(ipString);

        Assert.That(ip.IsLocalSubnet(), Is.EqualTo(expected));
        Assert.That(IPAddressExtensions.IsLocalSubnet(ipString), Is.EqualTo(expected));
    }

    [Test]
    public void IsLocalSubnet_IPv4_mapped_IPv6_evaluates_correctly()
    {
        var localMapped = IPAddress.Parse("::ffff:192.168.1.50");
        var publicMapped = IPAddress.Parse("::ffff:8.8.8.8");

        Assert.That(localMapped.IsLocalSubnet(), Is.True);
        Assert.That(publicMapped.IsLocalSubnet(), Is.False);
    }

    [Test]
    public void IsLocalSubnet_handles_null_and_empty()
    {
        IPAddress nullIp = null;
        Assert.That(nullIp.IsLocalSubnet(), Is.False);
        Assert.That(IPAddressExtensions.IsLocalSubnet((string)null), Is.False);
        Assert.That(IPAddressExtensions.IsLocalSubnet(string.Empty), Is.False);
        Assert.That(IPAddressExtensions.IsLocalSubnet("invalid-ip"), Is.False);
    }
}
