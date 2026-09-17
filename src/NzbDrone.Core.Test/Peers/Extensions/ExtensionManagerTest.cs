using System;
using System.IO;
using System.Net;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Simulation.ClientBehavior;

namespace NzbDrone.Core.Test.Peers.Extensions;

[TestFixture]
public class ExtensionManagerTest
{
    private IConfigService _configService;
    private ExtensionManager _manager;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.ExtensionFastExtension.Returns(true);
        _configService.ExtensionUtPex.Returns(true);
        _configService.ExtensionUtMetadata.Returns(true);
        _configService.ExtensionLtDontHave.Returns(true);
        _manager = new ExtensionManager(_configService);
    }

    [Test]
    public void FastExtensionEnabled_should_return_config_value()
    {
        _configService.ExtensionFastExtension.Returns(false);

        Assert.That(_manager.FastExtensionEnabled, Is.False);
    }

    [Test]
    public void GetSupportedExtensions_should_return_all_when_all_enabled()
    {
        var extensions = _manager.GetSupportedExtensions();

        Assert.That(extensions.Count, Is.EqualTo(3));
        Assert.That(extensions.ContainsKey("ut_pex"), Is.True);
        Assert.That(extensions.ContainsKey("ut_metadata"), Is.True);
        Assert.That(extensions.ContainsKey("lt_donthave"), Is.True);
    }

    [Test]
    public void GetSupportedExtensions_should_return_empty_when_none_enabled()
    {
        _configService.ExtensionUtPex.Returns(false);
        _configService.ExtensionUtMetadata.Returns(false);
        _configService.ExtensionLtDontHave.Returns(false);

        var extensions = _manager.GetSupportedExtensions();

        Assert.That(extensions, Is.Empty);
    }

    [Test]
    public void GetSupportedExtensions_should_include_ut_pex_when_enabled()
    {
        _configService.ExtensionUtMetadata.Returns(false);
        _configService.ExtensionLtDontHave.Returns(false);

        var extensions = _manager.GetSupportedExtensions();

        Assert.That(extensions.ContainsKey("ut_pex"), Is.True);
        Assert.That(extensions.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetSupportedExtensions_should_include_ut_metadata_when_enabled()
    {
        _configService.ExtensionUtPex.Returns(false);
        _configService.ExtensionLtDontHave.Returns(false);

        var extensions = _manager.GetSupportedExtensions();

        Assert.That(extensions.ContainsKey("ut_metadata"), Is.True);
        Assert.That(extensions.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetSupportedExtensions_should_include_lt_donthave_when_enabled()
    {
        _configService.ExtensionUtPex.Returns(false);
        _configService.ExtensionUtMetadata.Returns(false);

        var extensions = _manager.GetSupportedExtensions();

        Assert.That(extensions.ContainsKey("lt_donthave"), Is.True);
        Assert.That(extensions.Count, Is.EqualTo(1));
    }

    [Test]
    public void GetSupportedExtensions_should_assign_sequential_ids()
    {
        var extensions = _manager.GetSupportedExtensions();

        Assert.That(extensions["ut_pex"], Is.EqualTo(1));
        Assert.That(extensions["ut_metadata"], Is.EqualTo(2));
        Assert.That(extensions["lt_donthave"], Is.EqualTo(3));
    }

    [Test]
    public void BuildExtensionHandshake_should_return_bencoded_data()
    {
        var result = _manager.BuildExtensionHandshake();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.GreaterThan(0));
    }

    [Test]
    public void BuildExtensionHandshake_should_contain_m_key()
    {
        var result = _manager.BuildExtensionHandshake();
        var dict = ParseBencode(result);

        Assert.That(dict.ContainsKey("m"), Is.True);
    }

    [Test]
    public void BuildExtensionHandshake_should_include_enabled_extensions()
    {
        var result = _manager.BuildExtensionHandshake();
        var dict = ParseBencode(result);
        var mDict = (BDictionary)dict["m"];

        Assert.That(mDict.ContainsKey("ut_pex"), Is.True);
        Assert.That(mDict.ContainsKey("ut_metadata"), Is.True);
        Assert.That(mDict.ContainsKey("lt_donthave"), Is.True);
        Assert.That((int)((BNumber)mDict["ut_pex"]).Value, Is.EqualTo(1));
        Assert.That((int)((BNumber)mDict["ut_metadata"]).Value, Is.EqualTo(2));
        Assert.That((int)((BNumber)mDict["lt_donthave"]).Value, Is.EqualTo(3));
    }

    [Test]
    public void BuildExtensionHandshake_should_exclude_disabled_extensions()
    {
        _configService.ExtensionUtPex.Returns(false);
        _configService.ExtensionLtDontHave.Returns(false);

        var result = _manager.BuildExtensionHandshake();
        var dict = ParseBencode(result);
        var mDict = (BDictionary)dict["m"];

        Assert.That(mDict.ContainsKey("ut_pex"), Is.False);
        Assert.That(mDict.ContainsKey("ut_metadata"), Is.True);
        Assert.That(mDict.ContainsKey("lt_donthave"), Is.False);
    }

    [Test]
    public void GetSupportedExtensions_should_assign_sequential_ids_with_gaps()
    {
        _configService.ExtensionUtPex.Returns(false);

        var extensions = _manager.GetSupportedExtensions();

        Assert.That(extensions["ut_metadata"], Is.EqualTo(1));
        Assert.That(extensions["lt_donthave"], Is.EqualTo(2));
    }

    [Test]
    public void FastExtensionEnabled_should_return_true_when_config_enabled()
    {
        _configService.ExtensionFastExtension.Returns(true);

        Assert.That(_manager.FastExtensionEnabled, Is.True);
    }

    [Test]
    public void BuildExtensionHandshake_should_contain_v_key_with_default_client_version()
    {
        var result = _manager.BuildExtensionHandshake();
        var dict = ParseBencode(result);

        Assert.That(dict.ContainsKey("v"), Is.True);
        Assert.That(((BString)dict["v"]).ToString(), Does.StartWith("Seedarr/"));
    }

    [Test]
    public void BuildExtensionHandshake_should_populate_v_key_with_client_profile_user_agent()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.UserAgent.Returns("Transmission/3.00");

        var result = _manager.BuildExtensionHandshake(false, profile);
        var dict = ParseBencode(result);

        Assert.That(dict.ContainsKey("v"), Is.True);
        Assert.That(((BString)dict["v"]).ToString(), Is.EqualTo("Transmission/3.00"));
    }

    [Test]
    public void BuildExtensionHandshake_should_fallback_to_client_profile_name_when_user_agent_is_null()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.UserAgent.Returns((string)null);
        profile.Name.Returns("qBittorrent 4.4.2");

        var result = _manager.BuildExtensionHandshake(false, profile);
        var dict = ParseBencode(result);

        Assert.That(dict.ContainsKey("v"), Is.True);
        Assert.That(((BString)dict["v"]).ToString(), Is.EqualTo("qBittorrent 4.4.2"));
    }

    [Test]
    public void BuildExtensionHandshake_should_populate_context_fields()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.UserAgent.Returns("TestClient/2.0");

        var remoteIp = IPAddress.Parse("192.168.1.50");
        var result = _manager.BuildExtensionHandshake(
            remoteIp: remoteIp,
            listeningPort: 6881,
            metadataSize: 123456,
            isPrivate: false,
            clientProfile: profile);

        var dict = ParseBencode(result);

        Assert.That(dict.ContainsKey("m"), Is.True);
        Assert.That(dict.ContainsKey("v"), Is.True);
        Assert.That(((BString)dict["v"]).ToString(), Is.EqualTo("TestClient/2.0"));
        Assert.That(dict.ContainsKey("p"), Is.True);
        Assert.That((int)((BNumber)dict["p"]).Value, Is.EqualTo(6881));
        Assert.That(dict.ContainsKey("metadata_size"), Is.True);
        Assert.That(((BNumber)dict["metadata_size"]).Value, Is.EqualTo(123456));
        Assert.That(dict.ContainsKey("yourip"), Is.True);
        Assert.That(((BString)dict["yourip"]).Value.ToArray(), Is.EqualTo(remoteIp.GetAddressBytes()));
        Assert.That(dict.ContainsKey("reqq"), Is.True);
        Assert.That((int)((BNumber)dict["reqq"]).Value, Is.EqualTo(250));
    }

    [Test]
    public void BuildExtensionHandshake_should_omit_optional_fields_when_zero_or_null()
    {
        var result = _manager.BuildExtensionHandshake();
        var dict = ParseBencode(result);

        Assert.That(dict.ContainsKey("p"), Is.False);
        Assert.That(dict.ContainsKey("metadata_size"), Is.False);
        Assert.That(dict.ContainsKey("yourip"), Is.False);
        Assert.That(dict.ContainsKey("reqq"), Is.True);
    }

    [Test]
    public void BuildExtensionHandshake_should_use_peer_request_count_from_config()
    {
        _configService.PeerRequestCount.Returns(500);

        var result = _manager.BuildExtensionHandshake();
        var dict = ParseBencode(result);

        Assert.That((int)((BNumber)dict["reqq"]).Value, Is.EqualTo(500));
    }

    [Test]
    public void ParseExtensionHandshake_should_return_null_for_invalid_or_empty_data()
    {
        Assert.That(_manager.ParseExtensionHandshake((byte[])null), Is.Null);
        Assert.That(_manager.ParseExtensionHandshake((BDictionary)null), Is.Null);
        Assert.That(_manager.ParseExtensionHandshake(Array.Empty<byte>()), Is.Null);
        Assert.That(_manager.ParseExtensionHandshake(new byte[] { 0, 1, 2, 3 }), Is.Null);
    }

    [Test]
    public void ParseExtensionHandshake_should_parse_valid_handshake()
    {
        var profile = Substitute.For<IClientProfile>();
        profile.UserAgent.Returns("Transmission/4.0.0");

        var remoteIp = IPAddress.Parse("10.0.0.42");
        var bytes = _manager.BuildExtensionHandshake(
            remoteIp: remoteIp,
            listeningPort: 51413,
            metadataSize: 654321,
            isPrivate: false,
            clientProfile: profile);

        var parsed = _manager.ParseExtensionHandshake(bytes);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed.Version, Is.EqualTo("Transmission/4.0.0"));
        Assert.That(parsed.V, Is.EqualTo("Transmission/4.0.0"));
        Assert.That(parsed.Port, Is.EqualTo(51413));
        Assert.That(parsed.P, Is.EqualTo(51413));
        Assert.That(parsed.MetadataSize, Is.EqualTo(654321));
        Assert.That(parsed.YourIp, Is.EqualTo(remoteIp));
        Assert.That(parsed.RequestQueueLength, Is.EqualTo(250));
        Assert.That(parsed.Reqq, Is.EqualTo(250));
        Assert.That(parsed.Extensions.ContainsKey("ut_pex"), Is.True);
        Assert.That(parsed.M.ContainsKey("ut_metadata"), Is.True);
    }

    [Test]
    public void ParseExtensionHandshake_should_parse_bdictionary_directly()
    {
        var dict = new BDictionary
        {
            ["m"] = new BDictionary
            {
                ["ut_pex"] = new BNumber(1),
                ["ut_metadata"] = new BNumber(2)
            },
            ["v"] = new BString("libtorrent/2.0.8"),
            ["p"] = new BNumber(6881),
            ["reqq"] = new BNumber(300),
            ["metadata_size"] = new BNumber(1024),
            ["yourip"] = new BString(new byte[] { 192, 168, 1, 100 })
        };

        var parsed = _manager.ParseExtensionHandshake(dict);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed.Version, Is.EqualTo("libtorrent/2.0.8"));
        Assert.That(parsed.Port, Is.EqualTo(6881));
        Assert.That(parsed.RequestQueueLength, Is.EqualTo(300));
        Assert.That(parsed.MetadataSize, Is.EqualTo(1024));
        Assert.That(parsed.YourIp, Is.EqualTo(IPAddress.Parse("192.168.1.100")));
        Assert.That(parsed.Extensions["ut_pex"], Is.EqualTo(1));
        Assert.That(parsed.Extensions["ut_metadata"], Is.EqualTo(2));
    }

    private static BDictionary ParseBencode(byte[] data)
    {
        var parser = new BencodeParser();
        using var stream = new MemoryStream(data);
        return parser.Parse<BDictionary>(stream);
    }
}
