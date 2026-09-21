using System.Threading.Channels;

namespace Seedarr.Api.V1.Mcp;

public interface ISseSessionManager
{
    string CreateSession(Channel<string> channel);
    bool TryGetSession(string sessionId, out Channel<string> channel);
    void RemoveSession(string sessionId);
}
