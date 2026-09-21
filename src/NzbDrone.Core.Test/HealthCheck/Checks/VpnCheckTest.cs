using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class VpnCheckTest
{
    private IVpnKillSwitchService _vpnKillSwitchService;
    private VpnCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _vpnKillSwitchService = Substitute.For<IVpnKillSwitchService>();
        _subject = new VpnCheck(_vpnKillSwitchService);
    }

    [Test]
    public void Check_should_return_ok_when_service_is_null()
    {
        var check = new VpnCheck(null);
        var result = check.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(result.Source, Is.EqualTo(nameof(VpnCheck)));
    }

    [Test]
    public void Check_should_return_ok_when_kill_switch_is_disabled()
    {
        _vpnKillSwitchService.IsKillSwitchEnabled.Returns(false);
        _vpnKillSwitchService.State.Returns(VpnState.Disabled);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_should_return_ok_when_vpn_is_up_and_fail_closed_inactive()
    {
        _vpnKillSwitchService.IsKillSwitchEnabled.Returns(true);
        _vpnKillSwitchService.VpnInterfaceName.Returns("tun0");
        _vpnKillSwitchService.IsVpnInterfaceUp.Returns(true);
        _vpnKillSwitchService.IsFailClosedActive.Returns(false);
        _vpnKillSwitchService.State.Returns(VpnState.Up);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_should_return_error_when_vpn_interface_is_down()
    {
        _vpnKillSwitchService.IsKillSwitchEnabled.Returns(true);
        _vpnKillSwitchService.VpnInterfaceName.Returns("tun0");
        _vpnKillSwitchService.IsVpnInterfaceUp.Returns(false);
        _vpnKillSwitchService.IsFailClosedActive.Returns(true);
        _vpnKillSwitchService.State.Returns(VpnState.Down);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("tun0"));
        Assert.That(result.Message, Does.Contain("down"));
    }

    [Test]
    public void Check_should_return_warning_when_kill_switch_is_active_and_stabilizing()
    {
        _vpnKillSwitchService.IsKillSwitchEnabled.Returns(true);
        _vpnKillSwitchService.VpnInterfaceName.Returns("wg0");
        _vpnKillSwitchService.IsVpnInterfaceUp.Returns(true);
        _vpnKillSwitchService.IsFailClosedActive.Returns(true);
        _vpnKillSwitchService.IsStabilizing.Returns(true);
        _vpnKillSwitchService.State.Returns(VpnState.Stabilizing);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("wg0"));
        Assert.That(result.Message, Does.Contain("stabilizing"));
    }

    [Test]
    public void Check_should_return_error_when_fail_closed_is_active_and_not_stabilizing()
    {
        _vpnKillSwitchService.IsKillSwitchEnabled.Returns(true);
        _vpnKillSwitchService.VpnInterfaceName.Returns("tun0");
        _vpnKillSwitchService.IsVpnInterfaceUp.Returns(true);
        _vpnKillSwitchService.IsFailClosedActive.Returns(true);
        _vpnKillSwitchService.IsStabilizing.Returns(false);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("fail-closed"));
    }
}
