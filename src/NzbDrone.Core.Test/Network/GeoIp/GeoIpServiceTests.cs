using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Network.GeoIp;

namespace NzbDrone.Core.Test.Network.GeoIp;

[TestFixture]
public class GeoIpServiceTests
{
    private IAppFolderInfo _appFolderInfo;
    private GeoIpService _service;

    [SetUp]
    public void SetUp()
    {
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns("/fake/appdata");
        _appFolderInfo.StartUpFolder.Returns("/fake/startup");

        _service = new GeoIpService(_appFolderInfo);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Lookup_returns_null_for_null_or_whitespace_ip(string ip)
    {
        var result = _service.Lookup(ip);
        Assert.That(result, Is.Null);
    }

    [TestCase("not_an_ip")]
    [TestCase("999.999.999.999")]
    [TestCase("abc.def.ghi.jkl")]
    public void Lookup_returns_fallback_result_and_caches_for_invalid_ip(string invalidIp)
    {
        var initialCacheCount = _service.CacheCount;
        var result = _service.Lookup(invalidIp);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IpAddress, Is.EqualTo(invalidIp));
        Assert.That(result.CountryCode, Is.Null.Or.Empty);
        Assert.That(_service.CacheCount, Is.EqualTo(initialCacheCount + 1));

        var cachedResult = _service.Lookup(invalidIp);
        Assert.That(cachedResult, Is.SameAs(result));
    }

    [TestCase("10.0.0.1")]
    [TestCase("10.255.255.255")]
    [TestCase("192.168.0.1")]
    [TestCase("192.168.1.100")]
    [TestCase("127.0.0.1")]
    [TestCase("127.0.0.2")]
    [TestCase("172.16.0.1")]
    [TestCase("172.24.50.1")]
    [TestCase("172.31.255.255")]
    [TestCase("100.64.0.1")]
    [TestCase("100.127.255.255")]
    [TestCase("169.254.1.1")]
    [TestCase("0.0.0.0")]
    public void Lookup_identifies_private_ipv4_and_returns_lan_metadata(string privateIp)
    {
        var result = _service.Lookup(privateIp);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IpAddress, Is.EqualTo(privateIp));
        Assert.That(result.CountryCode, Is.EqualTo("LAN"));
        Assert.That(result.CountryName, Is.EqualTo("Local Network"));
        Assert.That(result.City, Is.EqualTo("Localhost"));
    }

    [TestCase("::1")]
    [TestCase("fe80::1")]
    [TestCase("fc00::1")]
    [TestCase("fd00::1")]
    [TestCase("ff02::1")]
    public void Lookup_identifies_private_and_loopback_ipv6_as_lan(string privateIpv6)
    {
        var result = _service.Lookup(privateIpv6);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IpAddress, Is.EqualTo(privateIpv6));
        Assert.That(result.CountryCode, Is.EqualTo("LAN"));
        Assert.That(result.CountryName, Is.EqualTo("Local Network"));
    }

    [TestCase("172.15.255.255")]
    [TestCase("172.32.0.1")]
    [TestCase("100.63.255.255")]
    [TestCase("100.128.0.1")]
    [TestCase("8.8.8.8")]
    [TestCase("1.1.1.1")]
    [TestCase("93.184.216.34")]
    public void IsPrivateOrLoopback_returns_false_for_public_ips(string publicIp)
    {
        Assert.That(GeoIpService.IsPrivateOrLoopback(publicIp), Is.False);
    }

    [TestCase("::ffff:192.168.1.1", true)]
    [TestCase("::ffff:8.8.8.8", false)]
    public void IsPrivateOrLoopback_handles_ipv4_mapped_ipv6(string mappedIp, bool expected)
    {
        Assert.That(GeoIpService.IsPrivateOrLoopback(mappedIp), Is.EqualTo(expected));
    }

    [Test]
    public void Caching_prevents_duplicate_lookups_and_ClearCache_resets()
    {
        Assert.That(_service.CacheCount, Is.EqualTo(0));

        var res1 = _service.Lookup("10.0.0.5");
        Assert.That(_service.CacheCount, Is.EqualTo(1));

        var res2 = _service.Lookup("10.0.0.5");
        Assert.That(_service.CacheCount, Is.EqualTo(1));
        Assert.That(res2, Is.SameAs(res1));

        _service.ClearCache();
        Assert.That(_service.CacheCount, Is.EqualTo(0));
    }

    [Test]
    public void DatabasePath_management_and_refresh()
    {
        var customPath = "/custom/mmdb/GeoLite2-City.mmdb";
        _service.SetDatabasePath(customPath);

        Assert.That(_service.GetDatabasePath(), Is.EqualTo(customPath));

        _service.ClearCache();
        Assert.That(_service.GetDatabasePath(), Is.Null);
    }

    [Test]
    public void Lookup_returns_ip_only_when_no_database_available()
    {
        var publicIp = "8.8.8.8";
        var result = _service.Lookup(publicIp);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IpAddress, Is.EqualTo(publicIp));
        Assert.That(result.CountryCode, Is.Null.Or.Empty);
    }

    [Test]
    public async Task LookupAsync_returns_consistent_result()
    {
        var asyncResult = await _service.LookupAsync("192.168.1.1");

        Assert.That(asyncResult, Is.Not.Null);
        Assert.That(asyncResult.CountryCode, Is.EqualTo("LAN"));
    }

    [Test]
    public void Dispose_cleans_up_and_is_idempotent()
    {
        _service.Lookup("10.0.0.1");
        Assert.That(_service.CacheCount, Is.GreaterThan(0));

        _service.Dispose();
        Assert.That(_service.CacheCount, Is.EqualTo(0));

        Assert.DoesNotThrow(() => _service.Dispose());

        var afterDispose = _service.Lookup("8.8.8.8");
        Assert.That(afterDispose, Is.Not.Null);
        Assert.That(afterDispose.IpAddress, Is.EqualTo("8.8.8.8"));
    }
}
