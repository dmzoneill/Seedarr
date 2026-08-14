using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Peers;

public class PeerConnectedEvent : IEvent
{
    public string InfoHash { get; }
    public string RemoteIp { get; }
    public int RemotePort { get; }

    public PeerConnectedEvent(string infoHash, string remoteIp, int remotePort)
    {
        InfoHash = infoHash;
        RemoteIp = remoteIp;
        RemotePort = remotePort;
    }
}

public class PeerDisconnectedEvent : IEvent
{
    public string InfoHash { get; }
    public string RemoteIp { get; }

    public PeerDisconnectedEvent(string infoHash, string remoteIp)
    {
        InfoHash = infoHash;
        RemoteIp = remoteIp;
    }
}
