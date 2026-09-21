using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Mcp;

public interface IMcpService
{
    Task<JsonRpcResponse> ProcessMessageAsync(JsonRpcRequest request, CancellationToken cancellationToken = default);
    Task<JsonRpcResponse> ProcessMessageJsonAsync(string requestJson, CancellationToken cancellationToken = default);
}
