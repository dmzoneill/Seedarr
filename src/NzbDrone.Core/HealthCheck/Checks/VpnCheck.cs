using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.HealthCheck.Checks;

public class VpnCheck : IProvideHealthCheck
{
    private readonly IVpnKillSwitchService _vpnKillSwitchService;

    public VpnCheck(IVpnKillSwitchService vpnKillSwitchService = null)
    {
        _vpnKillSwitchService = vpnKillSwitchService;
    }

    public HealthCheckResult Check()
    {
        if (_vpnKillSwitchService == null || !_vpnKillSwitchService.IsKillSwitchEnabled || _vpnKillSwitchService.State == VpnState.Disabled)
        {
            return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
        }

        var iface = string.IsNullOrWhiteSpace(_vpnKillSwitchService.VpnInterfaceName)
            ? "VPN"
            : _vpnKillSwitchService.VpnInterfaceName;

        if (!_vpnKillSwitchService.IsVpnInterfaceUp)
        {
            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                $"VPN interface '{iface}' is down. Network kill switch has paused torrent traffic to prevent leaks.");
        }

        if (_vpnKillSwitchService.IsFailClosedActive)
        {
            if (_vpnKillSwitchService.IsStabilizing)
            {
                return new HealthCheckResult(
                    GetType(),
                    HealthCheckResultType.Warning,
                    $"VPN interface '{iface}' is stabilizing. Torrent traffic remains temporarily paused.");
            }

            return new HealthCheckResult(
                GetType(),
                HealthCheckResultType.Error,
                $"VPN kill switch is active (fail-closed) on interface '{iface}'. Torrent traffic is halted.");
        }

        return new HealthCheckResult(GetType(), HealthCheckResultType.Ok);
    }
}
