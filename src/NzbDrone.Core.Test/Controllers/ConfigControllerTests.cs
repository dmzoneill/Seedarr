using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Security;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Controllers;

[TestFixture]
public class ConfigControllerTests
{
    private IConfigService _configService;
    private IConfigFileProvider _configFileProvider;
    private ICertificateManager _certificateManager;
    private GeneralConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _certificateManager = Substitute.For<ICertificateManager>();

        _controller = new GeneralConfigController(
            _configService,
            _configFileProvider,
            _certificateManager);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    [TestCase(70000)]
    public void SaveConfig_with_invalid_port_returns_bad_request(int invalidPort)
    {
        var resource = new GeneralConfigResource
        {
            Port = invalidPort,
            SslPort = 8443,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _configFileProvider.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    [TestCase(70000)]
    public void SaveConfig_with_invalid_ssl_port_returns_bad_request(int invalidSslPort)
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = invalidSslPort,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        _configFileProvider.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [Test]
    public void SaveConfig_with_port_and_ssl_port_collision_returns_bad_request()
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8080,
            BindAddress = "*",
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("cannot be the same"));
        _configFileProvider.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [TestCase("not-a-valid-ip")]
    [TestCase("999.999.999.999")]
    [TestCase("http://localhost")]
    [TestCase("256.1.1.1")]
    public void SaveConfig_with_invalid_bind_address_returns_bad_request(string invalidBindAddress)
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = invalidBindAddress,
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("Invalid BindAddress"));
        _configFileProvider.DidNotReceive().SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [TestCase("*")]
    [TestCase("0.0.0.0")]
    [TestCase("::")]
    [TestCase("localhost")]
    [TestCase("127.0.0.1")]
    [TestCase("192.168.1.50")]
    [TestCase("::1")]
    [TestCase("")]
    [TestCase(null)]
    public void SaveConfig_with_valid_bind_address_succeeds(string validBindAddress)
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = validBindAddress,
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Any<Dictionary<string, object>>());
    }

    [TestCase("seedarr/", "/seedarr")]
    [TestCase("/seedarr/", "/seedarr")]
    [TestCase("/seedarr", "/seedarr")]
    [TestCase("seedarr", "/seedarr")]
    [TestCase("  /seedarr/  ", "/seedarr")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase(null, "")]
    public void SaveConfig_normalizes_url_base(string inputUrlBase, string expectedNormalized)
    {
        var resource = new GeneralConfigResource
        {
            Port = 8080,
            SslPort = 8443,
            BindAddress = "*",
            UrlBase = inputUrlBase,
            WatchFolderScanIntervalSeconds = 10
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.UrlBase, Is.EqualTo(expectedNormalized));
        _configFileProvider.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["UrlBase"] == expectedNormalized));
    }

    [Test]
    public void IsValidBindAddress_validates_correctly()
    {
        Assert.That(GeneralConfigController.IsValidBindAddress("*"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("0.0.0.0"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("::"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("localhost"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("127.0.0.1"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress("fe80::1"), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress(""), Is.True);
        Assert.That(GeneralConfigController.IsValidBindAddress(null), Is.True);

        Assert.That(GeneralConfigController.IsValidBindAddress("invalid_hostname"), Is.False);
        Assert.That(GeneralConfigController.IsValidBindAddress("1.2.3.4.5"), Is.False);
    }

    [Test]
    public void NormalizeUrlBase_normalizes_correctly()
    {
        Assert.That(GeneralConfigController.NormalizeUrlBase("seedarr/"), Is.EqualTo("/seedarr"));
        Assert.That(GeneralConfigController.NormalizeUrlBase("/seedarr/"), Is.EqualTo("/seedarr"));
        Assert.That(GeneralConfigController.NormalizeUrlBase("seedarr"), Is.EqualTo("/seedarr"));
        Assert.That(GeneralConfigController.NormalizeUrlBase("/"), Is.EqualTo(string.Empty));
        Assert.That(GeneralConfigController.NormalizeUrlBase(""), Is.EqualTo(string.Empty));
        Assert.That(GeneralConfigController.NormalizeUrlBase(null), Is.EqualTo(string.Empty));
    }
}
