using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Network.Vpn;

public class VpnKillSwitchTriggeredEvent : IEvent
{
    public string InterfaceName { get; }

    public VpnKillSwitchTriggeredEvent(string interfaceName)
    {
        InterfaceName = interfaceName;
    }
}

public class VpnInterfaceRestoredEvent : IEvent
{
    public string InterfaceName { get; }

    public VpnInterfaceRestoredEvent(string interfaceName)
    {
        InterfaceName = interfaceName;
    }
}
