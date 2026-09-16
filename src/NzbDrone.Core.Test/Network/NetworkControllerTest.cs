using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
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

    [TestCase("tun0", NetworkInterfaceType.Ethernet, "", true)]
    [TestCase("wg0", NetworkInterfaceType.Ethernet, "", true)]
    [TestCase("tap0", NetworkInterfaceType.Ethernet, "", true)]
    [TestCase("ppp0", NetworkInterfaceType.Ethernet, "", true)]
    [TestCase("utun2", NetworkInterfaceType.Ethernet, "", true)]
    [TestCase("tailscale0", NetworkInterfaceType.Ethernet, "", true)]
    [TestCase("zt0", NetworkInterfaceType.Ethernet, "", true)]
    [TestCase("eth0", NetworkInterfaceType.Tunnel, "", true)]
    [TestCase("eth0", NetworkInterfaceType.Ppp, "", true)]
    [TestCase("eth0", NetworkInterfaceType.Ethernet, "WireGuard Tunnel Adapter", true)]
    [TestCase("eth0", NetworkInterfaceType.Ethernet, "TAP-Windows Adapter V9", true)]
    [TestCase("eth0", NetworkInterfaceType.Ethernet, "Intel Gigabit Ethernet", false)]
    [TestCase("enp3s0", NetworkInterfaceType.Ethernet, "", false)]
    public void IsVpnOrTunnel_should_detect_vpn_interfaces(string name, NetworkInterfaceType type, string description, bool expected)
    {
        var result = NetworkController.IsVpnOrTunnel(name, type, description);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("tun0", NetworkInterfaceType.Tunnel, true, "Tunnel")]
    [TestCase("wg0", NetworkInterfaceType.Ethernet, true, "Tunnel")]
    [TestCase("wlan0", NetworkInterfaceType.Ethernet, false, "Wireless")]
    [TestCase("wifi0", NetworkInterfaceType.Ethernet, false, "Wireless")]
    [TestCase("wl_home", NetworkInterfaceType.Ethernet, false, "Wireless")]
    [TestCase("nic1", NetworkInterfaceType.Wireless80211, false, "Wireless")]
    [TestCase("docker0", NetworkInterfaceType.Ethernet, false, "Virtual")]
    [TestCase("veth1234", NetworkInterfaceType.Ethernet, false, "Virtual")]
    [TestCase("br-lan", NetworkInterfaceType.Ethernet, false, "Virtual")]
    [TestCase("virbr0", NetworkInterfaceType.Ethernet, false, "Virtual")]
    [TestCase("vmnet8", NetworkInterfaceType.Ethernet, false, "Virtual")]
    [TestCase("eth0", NetworkInterfaceType.Ethernet, false, "Physical")]
    [TestCase("enp0s31f6", NetworkInterfaceType.Ethernet, false, "Physical")]
    public void ClassifyInterfaceType_should_classify_correctly(string name, NetworkInterfaceType type, bool isVpn, string expected)
    {
        var result = NetworkController.ClassifyInterfaceType(name, type, isVpn);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void GetInterfaces_should_return_ok_with_interfaces_list()
    {
        var actionResult = _subject.GetInterfaces();
        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());

        var okResult = (OkObjectResult)actionResult.Result;
        Assert.That(okResult.Value, Is.InstanceOf<List<NetworkInterfaceResource>>());
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
