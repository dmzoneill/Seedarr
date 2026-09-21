using System;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Seedarr.Api.V1.Mcp;

public class SseSessionManager : ISseSessionManager
{
    private readonly ConcurrentDictionary<string, Channel<string>> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public string CreateSession(Channel<string> channel)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = channel;
        return sessionId;
    }

    public bool TryGetSession(string sessionId, out Channel<string> channel)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            channel = null;
            return false;
        }

        return _sessions.TryGetValue(sessionId, out channel);
    }

    public void RemoveSession(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            _sessions.TryRemove(sessionId, out _);
        }
    }
}
