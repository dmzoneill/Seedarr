using System;
using System.Net;
using System.Net.Sockets;

namespace NzbDrone.Core.Network.Vpn;

public interface IVpnKillSwitchService : IDisposable
{
    bool IsKillSwitchEnabled { get; }

    string VpnInterfaceName { get; }

    bool IsVpnInterfaceUp { get; }

    bool IsFailClosedActive { get; }

    bool IsStabilizing { get; }

    VpnState State { get; }

    event Action<string> VpnDropped;

    event Action<string> VpnRestored;

    bool CheckVpnState();

    IPAddress GetVpnInterfaceIpAddress(AddressFamily family = AddressFamily.InterNetwork);
}
