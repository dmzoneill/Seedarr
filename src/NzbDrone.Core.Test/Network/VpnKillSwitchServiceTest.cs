using System;
using System.Net;
using System.Net.Sockets;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class VpnKillSwitchServiceTest
{
    private IEventAggregator _eventAggregator = null!;
    private IConfigService _configService = null!;
    private VpnKillSwitchService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _eventAggregator = Substitute.For<IEventAggregator>();
        _configService = Substitute.For<IConfigService>();

        _service = new VpnKillSwitchService(_configService, _eventAggregator);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
    }

    [Test]
    public void CheckVpnState_WhenKillSwitchDisabled_ReturnsFalseAndDoesNotEngageFailClosed()
    {
        _configService.EnableVpnKillSwitch.Returns(false);
        _configService.BindInterface.Returns("tun0");

        var isKillSwitchTriggered = _service.CheckVpnState();

        Assert.That(isKillSwitchTriggered, Is.False);
        Assert.That(_service.IsFailClosedActive, Is.False);
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnKillSwitchTriggeredEvent>());
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnInterfaceRestoredEvent>());
    }

    [Test]
    public void CheckVpnState_WhenInterfaceDrops_EngagesFailClosedAndPublishesTriggeredEvent()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("tun0");

        var vpnDroppedCalled = false;
        string droppedInterface = null;
        _service.VpnDropped += iface =>
        {
            vpnDroppedCalled = true;
            droppedInterface = iface;
        };

        // Simulate interface drop
        _service.InterfaceStatusCheck = _ => false;

        var triggered = _service.CheckVpnState();

        Assert.That(triggered, Is.True);
        Assert.That(_service.IsFailClosedActive, Is.True);
        Assert.That(vpnDroppedCalled, Is.True);
        Assert.That(droppedInterface, Is.EqualTo("tun0"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<VpnKillSwitchTriggeredEvent>(e => e.InterfaceName == "tun0"));
    }

    [Test]
    public void CheckVpnState_WhenInterfaceRestores_DisengagesFailClosedAndPublishesRestoredEvent()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("wg0");

        var restoredCalled = false;
        string restoredInterface = null;
        _service.VpnRestored += iface =>
        {
            restoredCalled = true;
            restoredInterface = iface;
        };

        // 1. First trigger drop
        _service.InterfaceStatusCheck = _ => false;
        _service.CheckVpnState();
        Assert.That(_service.IsFailClosedActive, Is.True);

        // 2. Now simulate interface restoration
        _service.InterfaceStatusCheck = _ => true;
        var triggered = _service.CheckVpnState();

        Assert.That(triggered, Is.False);
        Assert.That(_service.IsFailClosedActive, Is.False);
        Assert.That(restoredCalled, Is.True);
        Assert.That(restoredInterface, Is.EqualTo("wg0"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<VpnInterfaceRestoredEvent>(e => e.InterfaceName == "wg0"));
    }

    [Test]
    public void GetVpnInterfaceIpAddress_WhenFailClosedActive_ReturnsNull()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("tun0");

        var testIp = IPAddress.Parse("10.8.0.2");
        _service.InterfaceIpResolver = (_, _) => testIp;
        _service.InterfaceStatusCheck = _ => false;

        // Trigger drop
        _service.CheckVpnState();
        Assert.That(_service.IsFailClosedActive, Is.True);

        // When fail-closed is active, it must never return an IP address
        var resolvedIp = _service.GetVpnInterfaceIpAddress();
        Assert.That(resolvedIp, Is.Null);

        // Restore interface
        _service.InterfaceStatusCheck = _ => true;
        _service.CheckVpnState();
        Assert.That(_service.IsFailClosedActive, Is.False);

        // Now returns the VPN IP
        resolvedIp = _service.GetVpnInterfaceIpAddress();
        Assert.That(resolvedIp, Is.EqualTo(testIp));
    }

    [Test]
    public void GetVpnInterfaceIpAddress_WhenRequestingIPv6_ResolvesIPv6Address()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("tun0");

        var testIpv6 = IPAddress.Parse("2001:db8::1");
        _service.InterfaceIpResolver = (_, family) => family == AddressFamily.InterNetworkV6 ? testIpv6 : null;
        _service.InterfaceStatusCheck = _ => true;

        var resolvedIp = _service.GetVpnInterfaceIpAddress(AddressFamily.InterNetworkV6);
        Assert.That(resolvedIp, Is.EqualTo(testIpv6));
    }

    [Test]
    public void GetVpnInterfaceIpAddress_WhenResolvedIPv6IsLinkLocal_ReturnsNull()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("tun0");

        var linkLocalIpv6 = IPAddress.Parse("fe80::1");
        _service.InterfaceIpResolver = (_, family) => family == AddressFamily.InterNetworkV6 ? linkLocalIpv6 : null;
        _service.InterfaceStatusCheck = _ => true;

        var resolvedIp = _service.GetVpnInterfaceIpAddress(AddressFamily.InterNetworkV6);
        Assert.That(resolvedIp, Is.Null);
    }

    [Test]
    public void Handle_ConfigSavedEvent_TriggersCheckVpnState()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("tun0");

        // Simulate interface drop
        _service.InterfaceStatusCheck = _ => false;
        _service.Handle(new ConfigSavedEvent());

        Assert.That(_service.IsFailClosedActive, Is.True);
        _eventAggregator.Received(1).PublishEvent(Arg.Is<VpnKillSwitchTriggeredEvent>(e => e.InterfaceName == "tun0"));
    }

    [Test]
    public void Dispose_UnhooksEventHandlersAndDisposesTimerSafely()
    {
        _service.Dispose();
        Assert.That(_service.CheckVpnState(), Is.False);

        // Repeated dispose should be idempotent
        Assert.DoesNotThrow(() => _service.Dispose());
    }
}
