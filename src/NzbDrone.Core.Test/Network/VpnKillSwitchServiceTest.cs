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
    private DateTime _currentTime;

    [SetUp]
    public void SetUp()
    {
        _currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _eventAggregator = Substitute.For<IEventAggregator>();
        _configService = Substitute.For<IConfigService>();
        _configService.VpnStabilizationDelaySeconds.Returns(8);

        _service = new VpnKillSwitchService(_configService, _eventAggregator);
        _service.UtcNowProvider = () => _currentTime;
        _service.InterfaceEgressCheck = _ => true;
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
        Assert.That(_service.State, Is.EqualTo(VpnState.Disabled));
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
        Assert.That(_service.IsStabilizing, Is.False);
        Assert.That(_service.State, Is.EqualTo(VpnState.Down));
        Assert.That(vpnDroppedCalled, Is.True);
        Assert.That(droppedInterface, Is.EqualTo("tun0"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<VpnKillSwitchTriggeredEvent>(e => e.InterfaceName == "tun0"));
    }

    [Test]
    public void CheckVpnState_WhenInterfaceRestores_DoesNotImmediatelyFireBeforeHoldDownExpires()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("wg0");

        var restoredCalled = false;
        _service.VpnRestored += _ => restoredCalled = true;

        // 1. Trigger interface drop
        _service.InterfaceStatusCheck = _ => false;
        _service.CheckVpnState();
        Assert.That(_service.IsFailClosedActive, Is.True);
        Assert.That(_service.State, Is.EqualTo(VpnState.Down));

        // 2. Interface reports Up, entering hold-down timer
        _service.InterfaceStatusCheck = _ => true;
        var triggered = _service.CheckVpnState();

        Assert.That(triggered, Is.True);
        Assert.That(_service.IsFailClosedActive, Is.True);
        Assert.That(_service.IsStabilizing, Is.True);
        Assert.That(_service.State, Is.EqualTo(VpnState.Stabilizing));
        Assert.That(restoredCalled, Is.False);
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnInterfaceRestoredEvent>());
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnRestoredEvent>());

        // 3. Advance time partially (5s of 8s hold-down)
        _currentTime = _currentTime.AddSeconds(5);
        triggered = _service.CheckVpnState();

        Assert.That(triggered, Is.True);
        Assert.That(_service.IsFailClosedActive, Is.True);
        Assert.That(_service.IsStabilizing, Is.True);
        Assert.That(_service.State, Is.EqualTo(VpnState.Stabilizing));
        Assert.That(restoredCalled, Is.False);
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnInterfaceRestoredEvent>());
    }

    [Test]
    public void CheckVpnState_WhenHoldDownExpiresAndEgressCheckSucceeds_DisengagesFailClosedAndPublishesRestoredEvents()
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

        // 1. Drop
        _service.InterfaceStatusCheck = _ => false;
        _service.CheckVpnState();

        // 2. Up (enters stabilization)
        _service.InterfaceStatusCheck = _ => true;
        _service.CheckVpnState();

        // 3. Advance past 8s hold-down delay
        _currentTime = _currentTime.AddSeconds(8);
        var triggered = _service.CheckVpnState();

        Assert.That(triggered, Is.False);
        Assert.That(_service.IsFailClosedActive, Is.False);
        Assert.That(_service.IsStabilizing, Is.False);
        Assert.That(_service.State, Is.EqualTo(VpnState.Up));
        Assert.That(restoredCalled, Is.True);
        Assert.That(restoredInterface, Is.EqualTo("wg0"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<VpnInterfaceRestoredEvent>(e => e.InterfaceName == "wg0"));
        _eventAggregator.Received(1).PublishEvent(Arg.Is<VpnRestoredEvent>(e => e.InterfaceName == "wg0"));
    }

    [Test]
    public void CheckVpnState_WhenInterfaceFlapsDuringHoldDown_ResetsTimerAndPreventsPrematureRestoration()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("wg0");

        var restoredCalled = false;
        _service.VpnRestored += _ => restoredCalled = true;

        // 1. Drop
        _service.InterfaceStatusCheck = _ => false;
        _service.CheckVpnState();

        // 2. Up at t=0s
        _service.InterfaceStatusCheck = _ => true;
        _service.CheckVpnState();
        Assert.That(_service.IsStabilizing, Is.True);

        // 3. Flap: drops at t=5s
        _currentTime = _currentTime.AddSeconds(5);
        _service.InterfaceStatusCheck = _ => false;
        _service.CheckVpnState();
        Assert.That(_service.IsStabilizing, Is.False);
        Assert.That(_service.State, Is.EqualTo(VpnState.Down));

        // 4. Up again at t=6s (timer must reset!)
        _currentTime = _currentTime.AddSeconds(1);
        _service.InterfaceStatusCheck = _ => true;
        _service.CheckVpnState();
        Assert.That(_service.IsStabilizing, Is.True);

        // 5. At t=11s (5s after second Up, but 11s after initial Up): hold-down must NOT expire yet
        _currentTime = _currentTime.AddSeconds(5);
        _service.CheckVpnState();
        Assert.That(_service.IsFailClosedActive, Is.True);
        Assert.That(_service.IsStabilizing, Is.True);
        Assert.That(restoredCalled, Is.False);

        // 6. At t=14.1s (> 8s after second Up): hold-down expires
        _currentTime = _currentTime.AddSeconds(3.1);
        var triggered = _service.CheckVpnState();
        Assert.That(triggered, Is.False);
        Assert.That(_service.IsFailClosedActive, Is.False);
        Assert.That(_service.IsStabilizing, Is.False);
        Assert.That(_service.State, Is.EqualTo(VpnState.Up));
        Assert.That(restoredCalled, Is.True);
    }

    [Test]
    public void CheckVpnState_WhenHoldDownExpiresButEgressCheckFails_KeepsFailClosedActive()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("wg0");

        var restoredCalled = false;
        _service.VpnRestored += _ => restoredCalled = true;

        // 1. Drop
        _service.InterfaceStatusCheck = _ => false;
        _service.CheckVpnState();

        // 2. Up with failing egress check
        _service.InterfaceStatusCheck = _ => true;
        _service.InterfaceEgressCheck = _ => false;
        _service.CheckVpnState();

        // 3. Advance past hold-down timer
        _currentTime = _currentTime.AddSeconds(10);
        var triggered = _service.CheckVpnState();

        Assert.That(triggered, Is.True);
        Assert.That(_service.IsFailClosedActive, Is.True);
        Assert.That(_service.IsStabilizing, Is.True);
        Assert.That(restoredCalled, Is.False);
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnInterfaceRestoredEvent>());

        // 4. Egress check succeeds on subsequent evaluation
        _service.InterfaceEgressCheck = _ => true;
        triggered = _service.CheckVpnState();

        Assert.That(triggered, Is.False);
        Assert.That(_service.IsFailClosedActive, Is.False);
        Assert.That(_service.IsStabilizing, Is.False);
        Assert.That(_service.State, Is.EqualTo(VpnState.Up));
        Assert.That(restoredCalled, Is.True);
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

        // Restore interface with hold-down elapsed
        _service.InterfaceStatusCheck = _ => true;
        _service.CheckVpnState();
        _currentTime = _currentTime.AddSeconds(8);
        _service.CheckVpnState();
        Assert.That(_service.IsFailClosedActive, Is.False);

        // Now returns the VPN IP
        resolvedIp = _service.GetVpnInterfaceIpAddress();
        Assert.That(resolvedIp, Is.EqualTo(testIp));
    }

    [Test]
    public void GetVpnInterfaceIpAddress_WhenStabilizing_ReturnsNull()
    {
        _configService.EnableVpnKillSwitch.Returns(true);
        _configService.BindInterface.Returns("tun0");

        var testIp = IPAddress.Parse("10.8.0.2");
        _service.InterfaceIpResolver = (_, _) => testIp;

        // Trigger drop
        _service.InterfaceStatusCheck = _ => false;
        _service.CheckVpnState();

        // Interface Up but still stabilizing
        _service.InterfaceStatusCheck = _ => true;
        _service.CheckVpnState();

        Assert.That(_service.IsStabilizing, Is.True);
        Assert.That(_service.GetVpnInterfaceIpAddress(), Is.Null);
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
