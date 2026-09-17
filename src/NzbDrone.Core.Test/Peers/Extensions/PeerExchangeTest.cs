using System.Collections.Generic;
using System.Net;
using BencodeNET.Objects;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers.Extensions;

namespace NzbDrone.Core.Test.Peers.Extensions;

[TestFixture]
public class PeerExchangeTest
{
    private IConfigService _configService;
    private PeerExchange _peerExchange;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.EnablePex.Returns(true);
        _configService.PexInterval.Returns(60);
        _configService.PexMaxPeersPerMessage.Returns(50);
        _peerExchange = new PeerExchange(_configService);
    }

    [Test]
    public void IntervalSeconds_should_return_config_value()
    {
        _configService.PexInterval.Returns(120);

        Assert.That(_peerExchange.IntervalSeconds, Is.EqualTo(120));
    }

    [Test]
    public void BuildPexMessage_should_return_empty_when_pex_disabled()
    {
        _configService.EnablePex.Returns(false);

        var result = _peerExchange.BuildPexMessage(new List<PeerInfo>(), new List<PeerInfo>());

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildPexMessage_should_return_bencoded_data_when_pex_enabled()
    {
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "192.168.1.1", Port = 6881 }
        };

        var result = _peerExchange.BuildPexMessage(added, new List<PeerInfo>());

        Assert.That(result, Is.Not.Empty);
    }

    [Test]
    public void BuildPexMessage_should_cap_peers_to_max()
    {
        _configService.PexMaxPeersPerMessage.Returns(2);
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "10.0.0.1", Port = 6881 },
            new PeerInfo { Ip = "10.0.0.2", Port = 6882 },
            new PeerInfo { Ip = "10.0.0.3", Port = 6883 },
            new PeerInfo { Ip = "10.0.0.4", Port = 6884 }
        };

        var result = _peerExchange.BuildPexMessage(added, new List<PeerInfo>());
        var parsed = _peerExchange.ParsePexMessage(result);

        Assert.That(parsed.Added.Count, Is.EqualTo(2));
    }

    [Test]
    public void BuildPexMessage_should_handle_empty_lists()
    {
        var result = _peerExchange.BuildPexMessage(new List<PeerInfo>(), new List<PeerInfo>());

        Assert.That(result, Is.Not.Null);
        var parsed = _peerExchange.ParsePexMessage(result);
        Assert.That(parsed.Added, Is.Empty);
        Assert.That(parsed.Dropped, Is.Empty);
    }

    [Test]
    public void ParsePexMessage_should_return_empty_when_pex_disabled()
    {
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "192.168.1.1", Port = 6881 }
        };
        var encoded = _peerExchange.BuildPexMessage(added, new List<PeerInfo>());

        _configService.EnablePex.Returns(false);
        var result = _peerExchange.ParsePexMessage(encoded);

        Assert.That(result.Added, Is.Empty);
        Assert.That(result.Dropped, Is.Empty);
    }

    [Test]
    public void ParsePexMessage_should_parse_added_peers()
    {
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "192.168.1.1", Port = 6881 },
            new PeerInfo { Ip = "10.0.0.1", Port = 51413 }
        };

        var encoded = _peerExchange.BuildPexMessage(added, new List<PeerInfo>());
        var result = _peerExchange.ParsePexMessage(encoded);

        Assert.That(result.Added.Count, Is.EqualTo(2));
        Assert.That(result.Added[0].Ip, Is.EqualTo("192.168.1.1"));
        Assert.That(result.Added[0].Port, Is.EqualTo(6881));
        Assert.That(result.Added[1].Ip, Is.EqualTo("10.0.0.1"));
        Assert.That(result.Added[1].Port, Is.EqualTo(51413));
    }

    [Test]
    public void ParsePexMessage_should_parse_dropped_peers()
    {
        var dropped = new List<PeerInfo>
        {
            new PeerInfo { Ip = "172.16.0.1", Port = 8080 }
        };

        var encoded = _peerExchange.BuildPexMessage(new List<PeerInfo>(), dropped);
        var result = _peerExchange.ParsePexMessage(encoded);

        Assert.That(result.Dropped.Count, Is.EqualTo(1));
        Assert.That(result.Dropped[0].Ip, Is.EqualTo("172.16.0.1"));
        Assert.That(result.Dropped[0].Port, Is.EqualTo(8080));
    }

    [Test]
    public void ParsePexMessage_should_return_empty_on_invalid_data()
    {
        var result = _peerExchange.ParsePexMessage(new byte[] { 0xFF, 0xFE, 0xFD });

        Assert.That(result.Added, Is.Empty);
        Assert.That(result.Dropped, Is.Empty);
    }

    [Test]
    public void BuildPexMessage_roundtrip_should_preserve_peers()
    {
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "1.2.3.4", Port = 9999 },
            new PeerInfo { Ip = "5.6.7.8", Port = 12345 }
        };
        var dropped = new List<PeerInfo>
        {
            new PeerInfo { Ip = "9.10.11.12", Port = 80 }
        };

        var encoded = _peerExchange.BuildPexMessage(added, dropped);
        var parsed = _peerExchange.ParsePexMessage(encoded);

        Assert.That(parsed.Added.Count, Is.EqualTo(2));
        Assert.That(parsed.Added[0].Ip, Is.EqualTo("1.2.3.4"));
        Assert.That(parsed.Added[0].Port, Is.EqualTo(9999));
        Assert.That(parsed.Added[1].Ip, Is.EqualTo("5.6.7.8"));
        Assert.That(parsed.Added[1].Port, Is.EqualTo(12345));
        Assert.That(parsed.Dropped.Count, Is.EqualTo(1));
        Assert.That(parsed.Dropped[0].Ip, Is.EqualTo("9.10.11.12"));
        Assert.That(parsed.Dropped[0].Port, Is.EqualTo(80));
    }

    [Test]
    public void BuildPexMessage_should_encode_port_correctly()
    {
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "10.0.0.1", Port = 0x1F90 }
        };

        var encoded = _peerExchange.BuildPexMessage(added, new List<PeerInfo>());
        var parsed = _peerExchange.ParsePexMessage(encoded);

        Assert.That(parsed.Added[0].Port, Is.EqualTo(0x1F90));
    }

    [Test]
    public void ParsePexMessage_should_handle_missing_keys()
    {
        var dict = new BDictionary();
        var encoded = dict.EncodeAsBytes();

        var result = _peerExchange.ParsePexMessage(encoded);

        Assert.That(result.Added, Is.Empty);
        Assert.That(result.Dropped, Is.Empty);
    }

    [Test]
    public void BuildPexMessage_should_cap_dropped_peers_to_max()
    {
        _configService.PexMaxPeersPerMessage.Returns(1);
        var dropped = new List<PeerInfo>
        {
            new PeerInfo { Ip = "10.0.0.1", Port = 6881 },
            new PeerInfo { Ip = "10.0.0.2", Port = 6882 },
            new PeerInfo { Ip = "10.0.0.3", Port = 6883 }
        };

        var result = _peerExchange.BuildPexMessage(new List<PeerInfo>(), dropped);
        var parsed = _peerExchange.ParsePexMessage(result);

        Assert.That(parsed.Dropped.Count, Is.EqualTo(1));
    }

    [Test]
    public void BuildPexMessage_should_encode_high_port_big_endian()
    {
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "10.0.0.1", Port = 65535 }
        };

        var encoded = _peerExchange.BuildPexMessage(added, new List<PeerInfo>());
        var parsed = _peerExchange.ParsePexMessage(encoded);

        Assert.That(parsed.Added[0].Port, Is.EqualTo(65535));
    }

    [Test]
    public void IsRoutablePublicPeer_should_reject_invalid_port_or_null()
    {
        Assert.That(PeerExchange.IsRoutablePublicPeer(null, 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("8.8.8.8"), 0), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("8.8.8.8"), -1), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("8.8.8.8"), 65536), Is.False);
    }

    [Test]
    public void IsRoutablePublicPeer_should_reject_loopback_addresses()
    {
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("127.0.0.1"), 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("127.0.0.2"), 8989), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("127.255.255.254"), 7878), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.IPv6Loopback, 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("::ffff:127.0.0.1"), 6881), Is.False);
    }

    [Test]
    public void IsRoutablePublicPeer_should_reject_bogons_and_unroutable_ipv4()
    {
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Any, 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("0.0.0.1"), 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("169.254.1.1"), 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("169.254.254.254"), 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("224.0.0.1"), 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("239.255.255.250"), 1900), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("240.0.0.1"), 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Broadcast, 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.None, 6881), Is.False);
    }

    [Test]
    public void IsRoutablePublicPeer_should_reject_unroutable_ipv6()
    {
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.IPv6Any, 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("fe80::1"), 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("ff02::1"), 6881), Is.False);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("fec0::1"), 6881), Is.False);
    }

    [Test]
    public void IsRoutablePublicPeer_should_accept_valid_public_and_lan_ips()
    {
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("8.8.8.8"), 6881), Is.True);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("93.184.216.34"), 51413), Is.True);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("192.168.1.1"), 6881), Is.True);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("10.0.0.1"), 6881), Is.True);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("172.16.0.1"), 6881), Is.True);
        Assert.That(PeerExchange.IsRoutablePublicPeer(IPAddress.Parse("2001:4860:4860::8888"), 6881), Is.True);
    }

    [Test]
    public void ParseCompactPeers_should_reject_loopback_addresses()
    {
        // 127.0.0.1:8989
        var bytes = new byte[] { 127, 0, 0, 1, 0x23, 0x1D };
        var peers = PeerExchange.ParseCompactPeers(bytes);

        Assert.That(peers, Is.Empty);
    }

    [Test]
    public void ParseCompactPeers_should_reject_bogons()
    {
        // 0.0.0.1:6881, 169.254.1.1:6881, 224.0.0.1:6881, 255.255.255.255:6881
        var bytes = new byte[]
        {
            0, 0, 0, 1, 0x1A, 0xE1,
            169, 254, 1, 1, 0x1A, 0xE1,
            224, 0, 0, 1, 0x1A, 0xE1,
            255, 255, 255, 255, 0x1A, 0xE1
        };

        var peers = PeerExchange.ParseCompactPeers(bytes);

        Assert.That(peers, Is.Empty);
    }

    [Test]
    public void ParseCompactPeers_should_reject_port_zero()
    {
        // 93.184.216.34:0
        var bytes = new byte[] { 93, 184, 216, 34, 0, 0 };
        var peers = PeerExchange.ParseCompactPeers(bytes);

        Assert.That(peers, Is.Empty);
    }

    [Test]
    public void ParseCompactPeers_should_accept_valid_public_ipv4_and_ipv6_peers()
    {
        // IPv4: 93.184.216.34:6881
        var ipv4Bytes = new byte[] { 93, 184, 216, 34, 0x1A, 0xE1 };
        var ipv4Peers = PeerExchange.ParseCompactPeers(ipv4Bytes);

        Assert.That(ipv4Peers.Count, Is.EqualTo(1));
        Assert.That(ipv4Peers[0].Ip, Is.EqualTo("93.184.216.34"));
        Assert.That(ipv4Peers[0].Port, Is.EqualTo(6881));

        // IPv6: 2001:4860:4860::8888:6881
        var ip6 = IPAddress.Parse("2001:4860:4860::8888");
        var ipv6Bytes = new byte[18];
        ip6.GetAddressBytes().CopyTo(ipv6Bytes, 0);
        ipv6Bytes[16] = 0x1A;
        ipv6Bytes[17] = 0xE1;

        var ipv6Peers = PeerExchange.ParseCompactPeers(ipv6Bytes, isIPv6: true);
        Assert.That(ipv6Peers.Count, Is.EqualTo(1));
        Assert.That(ipv6Peers[0].Ip, Is.EqualTo("2001:4860:4860::8888"));
        Assert.That(ipv6Peers[0].Port, Is.EqualTo(6881));

        var ipv6PeersViaHelper = PeerExchange.ParseCompactPeers6(ipv6Bytes);
        Assert.That(ipv6PeersViaHelper.Count, Is.EqualTo(1));
        Assert.That(ipv6PeersViaHelper[0].Ip, Is.EqualTo("2001:4860:4860::8888"));
        Assert.That(ipv6PeersViaHelper[0].Port, Is.EqualTo(6881));
    }

    [Test]
    public void ParseCompactPeers_should_reject_self_connection_on_listening_port()
    {
        var listeningPort = 6881;
        // 127.0.0.1:6881
        var loopbackBytes = new byte[] { 127, 0, 0, 1, 0x1A, 0xE1 };
        var peers = PeerExchange.ParseCompactPeers(loopbackBytes, listeningPort: listeningPort);

        Assert.That(peers, Is.Empty);
    }

    [Test]
    public void ParsePexMessage_should_filter_out_loopback_and_bogon_peers_from_untrusted_peer()
    {
        var dict = new BDictionary
        {
            ["added"] = new BString(new byte[]
            {
                127, 0, 0, 1, 0x23, 0x1D, // 127.0.0.1:8989 (SSRF target)
                169, 254, 1, 1, 0x1A, 0xE1, // Link-Local
                93, 184, 216, 34, 0x1A, 0xE1 // Valid public peer
            }),
            ["dropped"] = new BString(new byte[]
            {
                0, 0, 0, 1, 0x1A, 0xE1 // Bogon
            })
        };

        var parsed = _peerExchange.ParsePexMessage(dict.EncodeAsBytes());

        Assert.That(parsed.Added.Count, Is.EqualTo(1));
        Assert.That(parsed.Added[0].Ip, Is.EqualTo("93.184.216.34"));
        Assert.That(parsed.Added[0].Port, Is.EqualTo(6881));
        Assert.That(parsed.Dropped, Is.Empty);
    }

    [Test]
    public void BuildPexMessage_and_ParsePexMessage_roundtrip_with_ipv6_peers()
    {
        var added = new List<PeerInfo>
        {
            new PeerInfo { Ip = "93.184.216.34", Port = 6881 },
            new PeerInfo { Ip = "2001:4860:4860::8888", Port = 51413 }
        };
        var dropped = new List<PeerInfo>
        {
            new PeerInfo { Ip = "2606:4700:4700::1111", Port = 8080 }
        };

        var encoded = _peerExchange.BuildPexMessage(added, dropped);
        var parsed = _peerExchange.ParsePexMessage(encoded);

        Assert.That(parsed.Added.Count, Is.EqualTo(2));
        Assert.That(parsed.Added[0].Ip, Is.EqualTo("93.184.216.34"));
        Assert.That(parsed.Added[0].Port, Is.EqualTo(6881));
        Assert.That(parsed.Added[1].Ip, Is.EqualTo("2001:4860:4860::8888"));
        Assert.That(parsed.Added[1].Port, Is.EqualTo(51413));

        Assert.That(parsed.Dropped.Count, Is.EqualTo(1));
        Assert.That(parsed.Dropped[0].Ip, Is.EqualTo("2606:4700:4700::1111"));
        Assert.That(parsed.Dropped[0].Port, Is.EqualTo(8080));
    }
}
