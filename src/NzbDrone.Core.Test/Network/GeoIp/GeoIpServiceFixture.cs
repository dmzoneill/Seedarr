using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Network.GeoIp;

namespace NzbDrone.Core.Test.Network.GeoIp;

[TestFixture]
public class GeoIpServiceFixture
{
    [TestCase("100.64.0.0", true)]
    [TestCase("100.64.0.1", true)]
    [TestCase("100.100.50.1", true)]
    [TestCase("100.127.255.254", true)]
    [TestCase("100.127.255.255", true)]
    [TestCase("100.63.255.255", false)]
    [TestCase("100.128.0.0", false)]
    [TestCase("100.128.0.1", false)]
    [TestCase("101.64.0.1", false)]
    [TestCase("99.64.0.1", false)]
    public void IsPrivateOrLoopback_evaluates_cgnat_correctly(string ipAddress, bool expected)
    {
        Assert.That(GeoIpService.IsPrivateOrLoopback(ipAddress), Is.EqualTo(expected));
    }

    [TestCase("10.0.0.1", true)]
    [TestCase("10.255.255.254", true)]
    [TestCase("172.16.0.1", true)]
    [TestCase("172.31.255.254", true)]
    [TestCase("172.15.255.255", false)]
    [TestCase("172.32.0.1", false)]
    [TestCase("192.168.0.1", true)]
    [TestCase("192.168.1.100", true)]
    [TestCase("127.0.0.1", true)]
    [TestCase("127.255.255.254", true)]
    [TestCase("169.254.1.1", true)]
    [TestCase("0.0.0.0", true)]
    [TestCase("192.0.2.1", true)]
    [TestCase("198.51.100.1", true)]
    [TestCase("203.0.113.1", true)]
    [TestCase("198.18.0.1", true)]
    [TestCase("198.19.255.254", true)]
    [TestCase("224.0.0.1", true)]
    [TestCase("239.255.255.255", true)]
    [TestCase("240.0.0.1", true)]
    [TestCase("255.255.255.255", true)]
    [TestCase("8.8.8.8", false)]
    [TestCase("1.1.1.1", false)]
    [TestCase("93.184.216.34", false)]
    [TestCase("::1", true)]
    [TestCase("::", true)]
    [TestCase("fe80::1", true)]
    [TestCase("fc00::1", true)]
    [TestCase("fd12:3456:789a::1", true)]
    [TestCase("ff02::1", true)]
    [TestCase("2001:4860:4860::8888", false)]
    [TestCase("::ffff:100.64.0.1", true)]
    [TestCase("::ffff:192.168.1.1", true)]
    [TestCase("::ffff:8.8.8.8", false)]
    public void IsPrivateOrLoopback_evaluates_special_and_private_subnets_correctly(string ipAddress, bool expected)
    {
        Assert.That(GeoIpService.IsPrivateOrLoopback(ipAddress), Is.EqualTo(expected));
    }

    [TestCase("100.64.1.1")]
    [TestCase("192.168.0.10")]
    [TestCase("10.0.0.5")]
    [TestCase("127.0.0.1")]
    public void Lookup_returns_lan_info_for_private_and_cgnat_ips(string ip)
    {
        using var service = new GeoIpService();
        var result = service.Lookup(ip);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IpAddress, Is.EqualTo(ip));
        Assert.That(result.CountryCode, Is.EqualTo("LAN"));
        Assert.That(result.CountryName, Is.EqualTo("Local Network"));
        Assert.That(result.City, Is.EqualTo("Localhost"));
    }

    [Test]
    public void Lookup_returns_null_for_null_or_whitespace()
    {
        using var service = new GeoIpService();
        Assert.That(service.Lookup(null), Is.Null);
        Assert.That(service.Lookup(string.Empty), Is.Null);
        Assert.That(service.Lookup("   "), Is.Null);
    }

    [Test]
    public void Lookup_returns_empty_geolocation_for_unparseable_ip()
    {
        using var service = new GeoIpService();
        var result = service.Lookup("not-an-ip");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.IpAddress, Is.EqualTo("not-an-ip"));
        Assert.That(result.CountryCode, Is.Empty);
    }

    [Test]
    public void Lookup_caches_ip_results_in_memory()
    {
        using var service = new GeoIpService();
        Assert.That(service.CacheCount, Is.EqualTo(0));

        var first = service.Lookup("100.64.0.1");
        Assert.That(service.CacheCount, Is.EqualTo(1));

        var second = service.Lookup("100.64.0.1");
        Assert.That(service.CacheCount, Is.EqualTo(1));
        Assert.That(second, Is.SameAs(first));

        var third = service.Lookup("8.8.8.8");
        Assert.That(service.CacheCount, Is.EqualTo(2));

        var fourth = service.Lookup("8.8.8.8");
        Assert.That(service.CacheCount, Is.EqualTo(2));
        Assert.That(fourth, Is.SameAs(third));

        service.ClearCache();
        Assert.That(service.CacheCount, Is.EqualTo(0));
    }

    [Test]
    public void GetDatabasePath_caches_resolved_path_in_memory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_geoip_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var dbDir = Path.Combine(tempDir, "GeoIP");
            Directory.CreateDirectory(dbDir);
            var dummyDbPath = Path.Combine(dbDir, "GeoLite2-City.mmdb");
            File.WriteAllText(dummyDbPath, "dummy");

            var appFolderInfo = Substitute.For<IAppFolderInfo>();
            appFolderInfo.AppDataFolder.Returns(tempDir);

            using var service = new GeoIpService(appFolderInfo);

            var path1 = service.GetDatabasePath();
            Assert.That(path1, Is.EqualTo(dummyDbPath));

            // Delete the file on disk to verify path was cached in memory
            File.Delete(dummyDbPath);
            Assert.That(File.Exists(dummyDbPath), Is.False);

            // Subsequent call returns cached path without re-scanning disk
            var path2 = service.GetDatabasePath();
            Assert.That(path2, Is.EqualTo(dummyDbPath));

            // Explicit refresh re-scans disk and detects file is gone
            var path3 = service.GetDatabasePath(refresh: true);
            Assert.That(path3, Is.Null);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task LookupAsync_returns_same_result_as_sync_lookup()
    {
        using var service = new GeoIpService();
        var syncResult = service.Lookup("100.64.1.2");
        var asyncResult = await service.LookupAsync("100.64.1.2");

        Assert.That(asyncResult, Is.SameAs(syncResult));
    }

    [Test]
    public void Dispose_does_not_throw_and_subsequent_lookups_return_safely()
    {
        var service = new GeoIpService();
        var beforeResult = service.Lookup("8.8.8.8");
        Assert.That(beforeResult, Is.Not.Null);

        service.Dispose();

        // Multiple calls to Dispose are safe
        Assert.DoesNotThrow(() => service.Dispose());

        // Lookups after disposal return safe fallback without throwing ObjectDisposedException
        Assert.DoesNotThrow(() =>
        {
            var afterResult = service.Lookup("8.8.8.8");
            Assert.That(afterResult, Is.Not.Null);
            Assert.That(afterResult.IpAddress, Is.EqualTo("8.8.8.8"));
        });
    }

    [Test]
    public void Concurrent_lookups_execute_safely_without_object_disposed_exception()
    {
        var service = new GeoIpService();
        var cts = new CancellationTokenSource();
        var tasks = new List<Task>();

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var ip = $"100.64.{index}.{Random.Shared.Next(1, 255)}";
                    var res = service.Lookup(ip);
                    Assert.That(res, Is.Not.Null);
                }
            }));
        }

        Thread.Sleep(50);
        service.ClearCache();
        Thread.Sleep(50);
        cts.Cancel();
        Task.WaitAll(tasks.ToArray());

        service.Dispose();
    }
}
