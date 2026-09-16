using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Dht;
using NzbDrone.Core.Network;
using NzbDrone.Core.Peers;
using Seedarr.Api.V1.Network;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class NetworkControllerTest
{
    private INetworkStatusService _networkStatusService;
    private IConnectionManager _connectionManager;
    private IDhtService _dhtService;
    private IConfigService _configService;
    private IPeerConnectionLogService _peerLogService;
    private NetworkController _subject;

    [SetUp]
    public void SetUp()
    {
        _networkStatusService = Substitute.For<INetworkStatusService>();
        _connectionManager = Substitute.For<IConnectionManager>();
        _dhtService = Substitute.For<IDhtService>();
        _configService = Substitute.For<IConfigService>();
        _peerLogService = Substitute.For<IPeerConnectionLogService>();

        _configService.ListeningPort.Returns(51413);

        _subject = new NetworkController(
            _networkStatusService,
            _connectionManager,
            _dhtService,
            _configService,
            _peerLogService);
    }

    [Test]
    public async Task TestPortAsync_with_explicit_port_should_call_service_and_return_ok()
    {
        var expected = new PortTestResult
        {
            Port = 6882,
            ExternalIp = "1.2.3.4",
            IsOpen = true,
            ResponseTime = TimeSpan.FromMilliseconds(50),
        };

        _networkStatusService.TestPortAsync(6882, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        var actionResult = await _subject.TestPortAsync(6882, CancellationToken.None);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)actionResult.Result;
        Assert.That(ok.Value, Is.SameAs(expected));
        await _networkStatusService.Received(1).TestPortAsync(6882, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TestPortAsync_with_null_port_should_use_listening_port()
    {
        var expected = new PortTestResult
        {
            Port = 51413,
            ExternalIp = "1.2.3.4",
            IsOpen = false,
            ErrorMessage = "Port reachability test timed out after 6 seconds.",
            ResponseTime = TimeSpan.FromSeconds(6),
        };

        _networkStatusService.TestPortAsync(51413, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expected));

        var actionResult = await _subject.TestPortAsync(null, CancellationToken.None);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var ok = (OkObjectResult)actionResult.Result;
        Assert.That(ok.Value, Is.SameAs(expected));
        await _networkStatusService.Received(1).TestPortAsync(51413, Arg.Any<CancellationToken>());
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    [TestCase(100000)]
    public async Task TestPortAsync_with_invalid_port_should_return_bad_request(int invalidPort)
    {
        var actionResult = await _subject.TestPortAsync(invalidPort, CancellationToken.None);

        Assert.That(actionResult.Result, Is.InstanceOf<BadRequestObjectResult>());
        await _networkStatusService.DidNotReceive().TestPortAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
