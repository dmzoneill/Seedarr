using System.Collections.Generic;
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
}
