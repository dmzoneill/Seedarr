using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Config;

[TestFixture]
public class NetworkConfigControllerTest
{
    private IConfigService _configService;
    private NetworkConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _controller = new NetworkConfigController(_configService);
    }

    [Test]
    public void SaveConfig_should_return_bad_request_when_MaxPerTorrentConnections_exceeds_MaxGlobalConnections()
    {
        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 50,
            MaxPerTorrentConnections = 200,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;

        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(NetworkConfigResource.MaxPerTorrentConnections) &&
            e.ErrorMessage.Contains("Max Connections Per Torrent cannot exceed Maximum Global Connections.")));
    }

    [Test]
    public void SaveConfig_should_succeed_when_MaxPerTorrentConnections_is_less_than_or_equal_to_MaxGlobalConnections()
    {
        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
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

        Assert.That(result.Result, Is.Null.Or.Not.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void SaveConfig_with_new_proxy_password_containing_asterisks_saves_password()
    {
        _configService.ProxyPassword.Returns("OldProxyPass123");

        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
            ProxyPassword = "*Proxy*Pass*",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ProxyPassword, Is.EqualTo("*Proxy*Pass*"));
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "*Proxy*Pass*"));
    }

    [Test]
    public void SaveConfig_with_exact_mask_preserves_existing_proxy_password()
    {
        _configService.ProxyPassword.Returns("ExistingProxySecret123");

        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
            ProxyPassword = "********",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ProxyPassword, Is.EqualTo("ExistingProxySecret123"));
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "ExistingProxySecret123"));
    }

    [Test]
    public void SaveConfig_with_unchanged_sentinel_preserves_existing_proxy_password()
    {
        _configService.ProxyPassword.Returns("ExistingProxySecret123");

        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
            ProxyPassword = "(unchanged)",
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ProxyPassword, Is.EqualTo("ExistingProxySecret123"));
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "ExistingProxySecret123"));
    }

    [Test]
    public void SaveConfig_with_null_preserves_existing_proxy_password()
    {
        _configService.ProxyPassword.Returns("ExistingProxySecret123");

        var resource = new NetworkConfigResource
        {
            ListeningPort = 51413,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 4,
            MaxConnectionsPerIp = 5,
            MaximumHalfOpenConnections = 50,
            PeerDscp = 0,
            PeerTos = 0,
            ProxyPort = 8080,
            ProxyPassword = null,
        };

        var result = _controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        Assert.That(resource.ProxyPassword, Is.EqualTo("ExistingProxySecret123"));
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "ExistingProxySecret123"));
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
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.Port.Returns(8989);
        configFileProvider.SslPort.Returns(9898);

        var controller = new NetworkConfigController(_configService, configFileProvider);

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

        var result = controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>().Or.InstanceOf<BadRequestResult>());
    }

    [Test]
    public void SaveConfig_should_reject_collision_with_web_server_SslPort()
    {
        var configFileProvider = Substitute.For<IConfigFileProvider>();
        configFileProvider.Port.Returns(8989);
        configFileProvider.SslPort.Returns(9898);

        var controller = new NetworkConfigController(_configService, configFileProvider);

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

        var result = controller.SaveConfig(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>().Or.InstanceOf<BadRequestResult>());
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
    public async Task TestProxy_should_call_proxy_test_service_and_return_result()
    {
        var proxyTestService = Substitute.For<IProxyTestService>();
        var expectedResult = new ProxyTestResult
        {
            Success = true,
            Message = "Proxy connection verified successfully.",
            RemoteDnsVerified = true
        };

        var request = new ProxyTestRequest
        {
            ProxyType = "socks5h",
            ProxyHost = "proxy.example.com",
            ProxyPort = 1080
        };

        proxyTestService.TestProxyAsync(request, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        var controller = new NetworkConfigController(_configService, null, proxyTestService);

        var result = await controller.TestProxy(request, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var actual = (ProxyTestResult)okResult.Value;
        Assert.That(actual.Success, Is.True);
        Assert.That(actual.RemoteDnsVerified, Is.True);
        Assert.That(actual.Message, Is.EqualTo("Proxy connection verified successfully."));
    }
}
