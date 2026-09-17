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

public class PeerRequestRejectedEvent : IEvent
{
    public PeerConnection Connection { get; }
    public string InfoHash => Connection?.InfoHash;
    public int PieceIndex { get; }
    public int Begin { get; }
    public int Length { get; }

    public PeerRequestRejectedEvent(PeerConnection connection, int pieceIndex, int begin, int length)
    {
        Connection = connection;
        PieceIndex = pieceIndex;
        Begin = begin;
        Length = length;
    }
}
