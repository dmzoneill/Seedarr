using System.IO;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers.Extensions;

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

    private static BDictionary ParseBencode(byte[] data)
    {
        var parser = new BencodeParser();
        using var stream = new MemoryStream(data);
        return parser.Parse<BDictionary>(stream);
    }
}
