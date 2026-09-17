using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Config;

namespace Seedarr.Api.V1.Test.Config;

[TestFixture]
public class ConfigControllerTest
{
    private IConfigService _configService;
    private IConfigFileProvider _configFileProvider;
    private NetworkConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configFileProvider = Substitute.For<IConfigFileProvider>();
        _configFileProvider.Port.Returns(8989);
        _configFileProvider.SslPort.Returns(9898);

        _controller = new NetworkConfigController(_configService, _configFileProvider);
    }

    [TestCase(0)]
    [TestCase(80)]
    [TestCase(443)]
    [TestCase(1023)]
    public void SaveConfig_should_reject_privileged_ports(int privilegedPort)
    {
        var resource = new NetworkConfigResource
        {
            ListeningPort = privilegedPort,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>().Or.InstanceOf<BadRequestResult>());
    }

    [Test]
    public void SaveConfig_should_reject_collision_with_web_server_Port()
    {
        var resource = new NetworkConfigResource
        {
            ListeningPort = 8989,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>().Or.InstanceOf<BadRequestResult>());
    }

    [Test]
    public void SaveConfig_should_reject_collision_with_web_server_SslPort()
    {
        var resource = new NetworkConfigResource
        {
            ListeningPort = 9898,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>().Or.InstanceOf<BadRequestResult>());
    }

    [Test]
    public void SaveConfig_should_accept_valid_non_privileged_non_conflicting_port()
    {
        var resource = new NetworkConfigResource
        {
            ListeningPort = 6881,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
    }
}
