using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;
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

    [Test]
    public async Task TestProxy_should_return_bad_request_when_request_is_null()
    {
        var result = await _controller.TestProxy(null, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var testResult = (ProxyTestResult)badRequest.Value;
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("Request body cannot be empty"));
    }

    [Test]
    public async Task TestProxy_should_delegate_to_proxy_test_service()
    {
        var proxyTestService = Substitute.For<IProxyTestService>();
        var expectedResult = new ProxyTestResult
        {
            Success = true,
            Message = "Proxy test passed.",
            RemoteDnsVerified = true
        };

        var request = new ProxyTestRequest
        {
            ProxyType = "socks5h",
            ProxyHost = "127.0.0.1",
            ProxyPort = 1080
        };

        proxyTestService.TestProxyAsync(request, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        var controller = new NetworkConfigController(_configService, _configFileProvider, proxyTestService);

        var result = await controller.TestProxy(request, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var actual = (ProxyTestResult)okResult.Value;
        Assert.That(actual.Success, Is.True);
        Assert.That(actual.RemoteDnsVerified, Is.True);
        Assert.That(actual.Message, Is.EqualTo("Proxy test passed."));
    }
}
