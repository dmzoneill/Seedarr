using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Network;

public interface IPortMappingEngine
{
    PortMappingProtocol CurrentProtocol { get; }
    Task<PortMappingProtocol> DetectProtocolAsync(CancellationToken cancellationToken = default);
    Task<PortMappingProtocol> ProbeAsync(CancellationToken cancellationToken = default);
    PortMappingProtocol DetectProtocol();
    Task<PortMappingStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<PortMappingStatus> RefreshAsync(CancellationToken cancellationToken = default);
}
