using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class NetworkSecurityServiceTest
{
    private IConfigService _configService;
    private IVpnKillSwitchService _vpnKillSwitchService;
    private NetworkSecurityService _subject;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _vpnKillSwitchService = Substitute.For<IVpnKillSwitchService>();
        _subject = new NetworkSecurityService(_configService, vpnKillSwitchService: _vpnKillSwitchService);
    }

    [Test]
    public void GetAvailableNetworkInterfaces_returns_non_loopback_names()
    {
        var interfaces = _subject.GetAvailableNetworkInterfaces().ToList();

        Assert.That(interfaces, Is.Not.Null);
        Assert.That(interfaces, Is.Not.Empty);
        Assert.That(interfaces.All(name => !string.IsNullOrWhiteSpace(name)), Is.True);
    }

    [Test]
    public void IsInterfaceActive_blank_name_treated_as_active()
    {
        Assert.That(_subject.IsInterfaceActive(null), Is.True);
        Assert.That(_subject.IsInterfaceActive("   "), Is.True);
    }

    [Test]
    public void IsInterfaceActive_uses_vpn_kill_switch_for_configured_vpn_interface()
    {
        _vpnKillSwitchService.VpnInterfaceName.Returns("tun0");
        _vpnKillSwitchService.IsVpnInterfaceUp.Returns(false);

        Assert.That(_subject.IsInterfaceActive("tun0"), Is.False);

        _vpnKillSwitchService.IsVpnInterfaceUp.Returns(true);
        Assert.That(_subject.IsInterfaceActive("TUN0"), Is.True);
    }

    [Test]
    public void IsInterfaceActive_matches_existing_interface_by_name()
    {
        var known = _subject.GetAvailableNetworkInterfaces().FirstOrDefault();
        if (known == null)
        {
            Assert.Ignore("No operational network interfaces on this host");
        }

        Assert.That(_subject.IsInterfaceActive(known), Is.True);
        Assert.That(_subject.IsInterfaceActive("definitely-missing-nic-12345"), Is.False);
    }

    [Test]
    public void CheckVpnKillSwitch_delegates_to_vpn_service()
    {
        _vpnKillSwitchService.CheckVpnState().Returns(true);
        Assert.That(_subject.CheckVpnKillSwitch(), Is.True);

        _vpnKillSwitchService.CheckVpnState().Returns(false);
        Assert.That(_subject.CheckVpnKillSwitch(), Is.False);
    }

    [Test]
    public void CheckVpnKillSwitch_without_vpn_service_returns_false()
    {
        var subject = new NetworkSecurityService(_configService);
        Assert.That(subject.CheckVpnKillSwitch(), Is.False);
    }
}
