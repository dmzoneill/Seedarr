using System.Net;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network;

public interface IGatewayDiscoveryService
{
    IPAddress GetDefaultGateway();
    Task<IPAddress> GetDefaultGatewayAsync();
}
