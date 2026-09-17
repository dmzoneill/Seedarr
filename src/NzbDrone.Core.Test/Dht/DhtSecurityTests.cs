using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using BencodeNET.Objects;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Dht;

namespace NzbDrone.Core.Test.Dht;

[TestFixture]
public class DhtSecurityTests
{
    private static readonly (string Ip, byte Rand, byte Exp0, byte Exp1, byte Exp2High5, byte ExpSuffix)[] TestVectors =
    {
        ("124.31.75.21", 1, 0x5f, 0xbf, 0xb8, 0x01),
        ("21.75.31.124", 86, 0x5a, 0x3c, 0xe8, 0x56),
        ("65.23.51.170", 22, 0xa5, 0xd4, 0x30, 0x16),
        ("84.124.73.14", 65, 0x1b, 0x03, 0x20, 0x41),
        ("43.213.53.83", 90, 0xe5, 0x6f, 0x68, 0x5a)
    };

    [Test]
    public void ComputeCrc32c_matches_BEP42_official_test_vectors()
    {
        foreach (var (ipStr, rand, exp0, exp1, exp2High5, expSuffix) in TestVectors)
        {
            var ip = IPAddress.Parse(ipStr);
            var crc = DhtSecurity.ComputeCrc32c(ip, rand);

            var b0 = (byte)((crc >> 24) & 0xFF);
            var b1 = (byte)((crc >> 16) & 0xFF);
            var b2High5 = (byte)((crc >> 8) & 0xF8);

            Assert.That(b0, Is.EqualTo(exp0), $"Byte 0 mismatch for IP {ipStr}");
            Assert.That(b1, Is.EqualTo(exp1), $"Byte 1 mismatch for IP {ipStr}");
            Assert.That(b2High5, Is.EqualTo(exp2High5), $"Byte 2 high 5 bits mismatch for IP {ipStr}");
            Assert.That(rand, Is.EqualTo(expSuffix), $"Suffix mismatch for IP {ipStr}");
        }
    }

    [Test]
    public void GenerateNodeId_creates_valid_BEP42_node_id_matching_vectors()
    {
        foreach (var (ipStr, rand, exp0, exp1, exp2High5, expSuffix) in TestVectors)
        {
            var ip = IPAddress.Parse(ipStr);
            var nodeId = DhtSecurity.GenerateNodeId(ip, rand);

            Assert.That(nodeId, Has.Length.EqualTo(20));
            Assert.That(nodeId[0], Is.EqualTo(exp0));
            Assert.That(nodeId[1], Is.EqualTo(exp1));
            Assert.That((byte)(nodeId[2] & 0xF8), Is.EqualTo(exp2High5));
            Assert.That(nodeId[19], Is.EqualTo(expSuffix));

            Assert.That(DhtSecurity.IsNodeIdValid(nodeId, ip), Is.True);
        }
    }

    [Test]
    public void IsNodeIdValid_returns_true_for_valid_node_id()
    {
        var ip = IPAddress.Parse("198.51.100.42");
        var nodeId = DhtSecurity.GenerateNodeId(ip);

        Assert.That(DhtSecurity.IsNodeIdValid(nodeId, ip), Is.True);
    }

    [Test]
    public void IsNodeIdValid_returns_false_for_tampered_node_id()
    {
        var ip = IPAddress.Parse("198.51.100.42");
        var nodeId = DhtSecurity.GenerateNodeId(ip);

        // Tamper byte 0
        var tampered0 = (byte[])nodeId.Clone();
        tampered0[0] ^= 0x01;
        Assert.That(DhtSecurity.IsNodeIdValid(tampered0, ip), Is.False);

        // Tamper byte 1
        var tampered1 = (byte[])nodeId.Clone();
        tampered1[1] ^= 0x01;
        Assert.That(DhtSecurity.IsNodeIdValid(tampered1, ip), Is.False);

        // Tamper byte 2 high bits
        var tampered2 = (byte[])nodeId.Clone();
        tampered2[2] ^= 0x80;
        Assert.That(DhtSecurity.IsNodeIdValid(tampered2, ip), Is.False);

        // Tamper byte 19 (rand)
        var tampered19 = (byte[])nodeId.Clone();
        tampered19[19] = (byte)(tampered19[19] + 1);
        Assert.That(DhtSecurity.IsNodeIdValid(tampered19, ip), Is.False);

        // Different IP address
        var otherIp = IPAddress.Parse("203.0.113.1");
        Assert.That(DhtSecurity.IsNodeIdValid(nodeId, otherIp), Is.False);
    }

    [Test]
    public void IsNodeIdValid_handles_invalid_arguments_gracefully()
    {
        var ip = IPAddress.Parse("124.31.75.21");
        var validId = DhtSecurity.GenerateNodeId(ip);

        Assert.That(DhtSecurity.IsNodeIdValid(null, ip), Is.False);
        Assert.That(DhtSecurity.IsNodeIdValid(new byte[19], ip), Is.False);
        Assert.That(DhtSecurity.IsNodeIdValid(new byte[21], ip), Is.False);
        Assert.That(DhtSecurity.IsNodeIdValid(validId, null), Is.False);
    }

    [Test]
    public void IsNodeIdValid_and_GenerateNodeId_support_IPv6()
    {
        var ipv6 = IPAddress.Parse("2001:db8:85a3::8a2e:370:7334");
        var nodeId = DhtSecurity.GenerateNodeId(ipv6, 42);

        Assert.That(nodeId, Has.Length.EqualTo(20));
        Assert.That(nodeId[19], Is.EqualTo(42));
        Assert.That(DhtSecurity.IsNodeIdValid(nodeId, ipv6), Is.True);

        var tampered = (byte[])nodeId.Clone();
        tampered[0] ^= 0x02;
        Assert.That(DhtSecurity.IsNodeIdValid(tampered, ipv6), Is.False);
    }

    [Test]
    public void DhtService_persists_generated_node_id_and_reloads_on_restart()
    {
        var configService = Substitute.For<IConfigService>();
        string storedHex = null;
        configService.DhtNodeIdHex.Returns(_ => storedHex);
        configService.When(c => c.DhtNodeIdHex = Arg.Any<string>())
                     .Do(call => storedHex = call.Arg<string>());

        configService.DhtBucketSize.Returns(8);
        configService.DhtRoutingTableSize.Returns(160);
        configService.DhtMaxNodes.Returns(1000);
        configService.DhtConcurrentQueries.Returns(3);
        configService.EnableDht.Returns(false);

        // First startup: no persisted hex
        using var service1 = new DhtService(configService);
        var firstNodeId = service1.NodeId;

        Assert.That(firstNodeId, Is.Not.Null);
        Assert.That(firstNodeId, Has.Length.EqualTo(20));
        Assert.That(storedHex, Is.EqualTo(Convert.ToHexString(firstNodeId)));

        // Second startup: reloads persisted hex
        using var service2 = new DhtService(configService);
        var secondNodeId = service2.NodeId;

        Assert.That(secondNodeId, Is.EqualTo(firstNodeId));
    }

    [Test]
    public void DhtService_updates_node_id_when_external_ip_is_resolved()
    {
        var configService = Substitute.For<IConfigService>();
        string storedHex = null;
        configService.DhtNodeIdHex.Returns(_ => storedHex);
        configService.When(c => c.DhtNodeIdHex = Arg.Any<string>())
                     .Do(call => storedHex = call.Arg<string>());

        configService.DhtBucketSize.Returns(8);
        configService.DhtRoutingTableSize.Returns(160);
        configService.DhtMaxNodes.Returns(1000);
        configService.DhtConcurrentQueries.Returns(3);
        configService.EnableDht.Returns(false);

        using var service = new DhtService(configService);
        var externalIp = IPAddress.Parse("124.31.75.21");

        service.UpdateExternalAddress(externalIp);

        Assert.That(DhtSecurity.IsNodeIdValid(service.NodeId, externalIp), Is.True);
        Assert.That(storedHex, Is.EqualTo(Convert.ToHexString(service.NodeId)));
    }

    [Test]
    public void DhtService_HandleQuery_rejects_invalid_node_id_from_public_ip()
    {
        var configService = Substitute.For<IConfigService>();
        configService.DhtBucketSize.Returns(8);
        configService.DhtRoutingTableSize.Returns(160);
        configService.DhtMaxNodes.Returns(1000);
        configService.DhtConcurrentQueries.Returns(3);
        configService.EnableDht.Returns(false);

        using var service = new DhtService(configService);
        service.RoutingTable.AllowLocal = true;

        var publicIp = IPAddress.Parse("124.31.75.21");
        var publicSender = new IPEndPoint(publicIp, 6881);

        // Invalid (non-BEP42) node ID for 124.31.75.21
        var invalidNodeId = new byte[20];
        Array.Fill<byte>(invalidNodeId, 0xAA);

        var query = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x01 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("ping"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(invalidNodeId)
            }
        };

        var handleMethod = typeof(DhtService).GetMethod("HandleMessage", BindingFlags.NonPublic | BindingFlags.Instance);
        handleMethod.Invoke(service, new object[] { query.EncodeAsBytes(), publicSender });

        // Node with invalid ID must NOT be added to routing table
        Assert.That(service.RoutingTable.NodeCount, Is.EqualTo(0));

        // Now send query with valid BEP42 node ID for 124.31.75.21
        var validNodeId = DhtSecurity.GenerateNodeId(publicIp, 1);
        var validQuery = new BDictionary
        {
            ["t"] = new BString(new byte[] { 0x02 }),
            ["y"] = new BString("q"),
            ["q"] = new BString("ping"),
            ["a"] = new BDictionary
            {
                ["id"] = new BString(validNodeId)
            }
        };

        handleMethod.Invoke(service, new object[] { validQuery.EncodeAsBytes(), publicSender });

        // Node with valid ID must be added to routing table
        Assert.That(service.RoutingTable.NodeCount, Is.EqualTo(1));
    }
}
