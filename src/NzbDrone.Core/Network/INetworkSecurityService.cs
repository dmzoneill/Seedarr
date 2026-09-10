using System.Collections.Generic;

namespace NzbDrone.Core.Network;

public interface INetworkSecurityService
{
    IEnumerable<string> GetAvailableNetworkInterfaces();

    bool IsInterfaceActive(string interfaceName);

    bool CheckVpnKillSwitch();
}
