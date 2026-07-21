using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Dht;

public interface IDhtService
{
    RoutingTable RoutingTable { get; }
    DhtPeerStore PeerStore { get; }
    Task SendGetPeers(IPEndPoint target, byte[] infoHash, CancellationToken ct = default);
    Task SendAnnouncePeer(IPEndPoint target, byte[] infoHash, int port, byte[] token, bool impliedPort = false, CancellationToken ct = default);
}
