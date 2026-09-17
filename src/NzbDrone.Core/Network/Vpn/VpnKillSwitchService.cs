using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Vpn;

public class VpnKillSwitchService : IVpnKillSwitchService, IHandle<ConfigSavedEvent>
{
    private readonly IConfigService _configService;
    private readonly IEventAggregator _eventAggregator;
    private readonly ISystemClock _clock;
    private readonly Logger _logger;
    private readonly object _stateLock = new();

    private Timer _heartbeatTimer;
    private bool _isFailClosedActive;
    private bool _lastKnownInterfaceUp = true;
    private DateTime? _interfaceUpSince;
    private bool _disposed;

    public event Action<string> VpnDropped;
    public event Action<string> VpnRestored;

    public Func<string, bool> InterfaceStatusCheck { get; set; }
    public Func<string, AddressFamily, IPAddress> InterfaceIpResolver { get; set; }
    public Func<string, bool> InterfaceEgressCheck { get; set; }
    public Func<DateTime> UtcNowProvider { get; set; }

    public VpnKillSwitchService(
        IConfigService configService,
        IEventAggregator eventAggregator = null,
        ISystemClock clock = null)
    {
        _configService = configService;
        _eventAggregator = eventAggregator;
        _clock = clock ?? new SystemClock();
        _logger = LogManager.GetCurrentClassLogger();

        InterfaceStatusCheck = CheckInterfaceStatusDefault;
        InterfaceIpResolver = ResolveInterfaceIpDefault;
        InterfaceEgressCheck = CheckInterfaceEgressDefault;
        UtcNowProvider = () => _clock.UtcNow;

        try
        {
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to subscribe to OS NetworkChange events. Falling back to heartbeat timer.");
        }

        _heartbeatTimer = new Timer(
            _ =>
            {
                try
                {
                    CheckVpnState();
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Exception during VPN heartbeat timer check");
                }
            },
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(1500));

        try
        {
            CheckVpnState();
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Exception during initial VPN state check");
        }
    }

    public bool IsKillSwitchEnabled => _configService.EnableVpnKillSwitch;

    public string VpnInterfaceName => _configService.BindInterface?.Trim() ?? string.Empty;

    public bool IsVpnInterfaceUp
    {
        get
        {
            var iface = VpnInterfaceName;
            return string.IsNullOrWhiteSpace(iface) || InterfaceStatusCheck(iface);
        }
    }

    public bool IsFailClosedActive
    {
        get
        {
            lock (_stateLock)
            {
                return _isFailClosedActive;
            }
        }
    }

    public bool IsStabilizing
    {
        get
        {
            lock (_stateLock)
            {
                return _isFailClosedActive && _interfaceUpSince != null;
            }
        }
    }

    public VpnState State
    {
        get
        {
            lock (_stateLock)
            {
                var enabled = _configService.EnableVpnKillSwitch;
                var iface = _configService.BindInterface?.Trim();

                if (!enabled || string.IsNullOrWhiteSpace(iface) ||
                    string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(iface, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(iface, "::", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(iface, "*", StringComparison.OrdinalIgnoreCase))
                {
                    return VpnState.Disabled;
                }

                if (!_isFailClosedActive)
                {
                    return VpnState.Up;
                }

                return _interfaceUpSince != null ? VpnState.Stabilizing : VpnState.Down;
            }
        }
    }

    public bool CheckVpnState()
    {
        if (_disposed)
        {
            return false;
        }

        lock (_stateLock)
        {
            var enabled = _configService.EnableVpnKillSwitch;
            var iface = _configService.BindInterface?.Trim();

            if (!enabled || string.IsNullOrWhiteSpace(iface) ||
                string.Equals(iface, "Any", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(iface, "all", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(iface, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(iface, "::", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(iface, "*", StringComparison.OrdinalIgnoreCase))
            {
                if (_isFailClosedActive)
                {
                    _isFailClosedActive = false;
                    _interfaceUpSince = null;
                    _logger.Info("VPN Kill switch disabled or binding unconfigured. Disengaging fail-closed state.");

                    try
                    {
                        VpnRestored?.Invoke(iface);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error in VpnRestored subscriber callback");
                    }

                    _eventAggregator?.PublishEvent(new VpnInterfaceRestoredEvent(iface ?? string.Empty));
                    _eventAggregator?.PublishEvent(new VpnRestoredEvent(iface ?? string.Empty));
                }

                _lastKnownInterfaceUp = true;
                _interfaceUpSince = null;
                return false;
            }

            var isUp = InterfaceStatusCheck(iface);

            if (!isUp)
            {
                _interfaceUpSince = null;

                if (!_isFailClosedActive || _lastKnownInterfaceUp)
                {
                    _isFailClosedActive = true;
                    _lastKnownInterfaceUp = false;
                    _logger.Error("VPN Kill Switch Triggered! Interface '{0}' dropped or unavailable. Halting all BitTorrent network traffic (fail-closed).", iface);

                    try
                    {
                        VpnDropped?.Invoke(iface);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error in VpnDropped subscriber callback");
                    }

                    _eventAggregator?.PublishEvent(new VpnKillSwitchTriggeredEvent(iface));
                }

                return true;
            }
            else
            {
                if (_isFailClosedActive || !_lastKnownInterfaceUp)
                {
                    var now = UtcNowProvider();
                    var delayConfig = _configService?.VpnStabilizationDelaySeconds ?? 8;
                    var stabilizationDelay = delayConfig > 0 ? delayConfig : 8;

                    if (_interfaceUpSince == null)
                    {
                        _interfaceUpSince = now;
                        _logger.Info("VPN interface '{0}' detected Up. Entering stabilization hold-down period ({1}s).", iface, stabilizationDelay);
                        return true;
                    }

                    var elapsed = (now - _interfaceUpSince.Value).TotalSeconds;
                    if (elapsed < stabilizationDelay)
                    {
                        _logger.Debug("VPN interface '{0}' stabilizing ({1:0.0}s / {2}s elapsed). Fail-closed remains active.", iface, elapsed, stabilizationDelay);
                        return true;
                    }

                    var egressHealthy = false;
                    try
                    {
                        egressHealthy = InterfaceEgressCheck(iface);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Exception during egress health check for interface '{0}'", iface);
                        egressHealthy = false;
                    }

                    if (!egressHealthy)
                    {
                        _logger.Warn("VPN interface '{0}' hold-down timer elapsed, but egress health check failed. Fail-closed remains active.", iface);
                        return true;
                    }

                    _isFailClosedActive = false;
                    _lastKnownInterfaceUp = true;
                    _interfaceUpSince = null;
                    _logger.Info("VPN interface '{0}' verified online, stable, and operational. Disengaging fail-closed state.", iface);

                    try
                    {
                        VpnRestored?.Invoke(iface);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error in VpnRestored subscriber callback");
                    }

                    _eventAggregator?.PublishEvent(new VpnInterfaceRestoredEvent(iface));
                    _eventAggregator?.PublishEvent(new VpnRestoredEvent(iface));
                }

                return false;
            }
        }
    }

    public IPAddress GetVpnInterfaceIpAddress(AddressFamily family = AddressFamily.InterNetwork)
    {
        var iface = VpnInterfaceName;
        if (string.IsNullOrWhiteSpace(iface))
        {
            return null;
        }

        if (IsFailClosedActive)
        {
            return null;
        }

        var ip = InterfaceIpResolver(iface, family);
        if (ip != null && family == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast ||
                IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None))
            {
                return null;
            }
        }

        return ip;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            try
            {
                NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
                NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            }
            catch
            {
            }

            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }
    }

    private void OnNetworkChanged(object sender, EventArgs e)
    {
        _logger.Debug("OS NetworkAddressChanged event detected. Validating VPN kill switch state immediately.");
        CheckVpnState();
    }

    private void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
    {
        _logger.Debug("OS NetworkAvailabilityChanged event detected (IsAvailable={0}). Validating VPN kill switch state immediately.", e.IsAvailable);
        CheckVpnState();
    }

    public void Handle(ConfigSavedEvent message)
    {
        CheckVpnState();
    }

    private bool CheckInterfaceStatusDefault(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return true;
        }

        // If interfaceName is an IP address, check if any active interface owns it
        if (IPAddress.TryParse(interfaceName, out var targetIp))
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                return interfaces.Any(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    nic.GetIPProperties()?.UnicastAddresses.Any(a => a.Address.Equals(targetIp)) == true);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to query network interfaces for IP '{0}'", interfaceName);
                return false;
            }
        }

        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (nic == null || nic.OperationalStatus != OperationalStatus.Up)
            {
                return false;
            }

            var unicast = nic.GetIPProperties()?.UnicastAddresses;
            return unicast != null && unicast.Any(a =>
                !IPAddress.IsLoopback(a.Address) &&
                !a.Address.Equals(IPAddress.Any) &&
                !a.Address.Equals(IPAddress.None) &&
                !a.Address.Equals(IPAddress.IPv6Any) &&
                !a.Address.Equals(IPAddress.IPv6None) &&
                !a.Address.IsIPv6LinkLocal &&
                !a.Address.IsIPv6SiteLocal &&
                !a.Address.IsIPv6Multicast);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to query network interface status for '{0}'", interfaceName);
            return false;
        }
    }

    internal IPAddress ResolveInterfaceIpDefault(string interfaceName, AddressFamily family)
    {
        try
        {
            if (IPAddress.TryParse(interfaceName, out var parsedIp))
            {
                if (parsedIp.AddressFamily == family)
                {
                    return parsedIp;
                }
            }

            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (nic == null || nic.OperationalStatus != OperationalStatus.Up)
            {
                return null;
            }

            var props = nic.GetIPProperties();
            if (props == null || props.UnicastAddresses == null)
            {
                return null;
            }

            var matching = props.UnicastAddresses
                .Select(u => u.Address)
                .FirstOrDefault(a => a.AddressFamily == family &&
                    !IPAddress.IsLoopback(a) &&
                    !a.Equals(IPAddress.Any) &&
                    !a.Equals(IPAddress.None) &&
                    !a.Equals(IPAddress.IPv6Any) &&
                    !a.Equals(IPAddress.IPv6None) &&
                    !a.IsIPv6LinkLocal &&
                    !a.IsIPv6SiteLocal &&
                    !a.IsIPv6Multicast);

            return matching;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to resolve IP for interface '{0}'", interfaceName);
            return null;
        }
    }

    private bool CheckInterfaceEgressDefault(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return true;
        }

        try
        {
            var ip = ResolveInterfaceIpDefault(interfaceName, AddressFamily.InterNetwork) ??
                     ResolveInterfaceIpDefault(interfaceName, AddressFamily.InterNetworkV6);

            if (ip == null)
            {
                _logger.Debug("Egress check failed: No valid IP address found for interface '{0}'", interfaceName);
                return false;
            }

            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Id, interfaceName, StringComparison.OrdinalIgnoreCase));

            if (nic != null && nic.OperationalStatus != OperationalStatus.Up)
            {
                _logger.Debug("Egress check failed: Interface '{0}' OperationalStatus is {1}", interfaceName, nic.OperationalStatus);
                return false;
            }

            using (var socket = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(ip, 0));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed egress health check for interface '{0}'", interfaceName);
            return false;
        }
    }
}
