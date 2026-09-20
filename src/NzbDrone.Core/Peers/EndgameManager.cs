using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Peers;

public interface IEndgameManager
{
    bool IsInEndgame(int missingBlockCount, int inFlightRequestsCount);
    void RegisterRequest(int torrentId, int pieceIndex, int begin, int length, PeerConnection peer);
    List<PeerConnection> OnBlockReceived(int torrentId, int pieceIndex, int begin, int length, PeerConnection sourcePeer);
    bool HasRequested(int torrentId, int pieceIndex, int begin, int length, PeerConnection peer);
    void ClearTorrent(int torrentId);
    void UnregisterPeer(PeerConnection peer);
}

public class EndgameManager : IEndgameManager
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<(int PieceIndex, int Begin, int Length), HashSet<PeerConnection>>> _torrentRequests = new();

    public bool IsInEndgame(int missingBlockCount, int inFlightRequestsCount)
    {
        return missingBlockCount > 0 && missingBlockCount <= inFlightRequestsCount;
    }

    public void RegisterRequest(int torrentId, int pieceIndex, int begin, int length, PeerConnection peer)
    {
        if (peer == null)
        {
            return;
        }

        var blockRequests = _torrentRequests.GetOrAdd(torrentId, _ => new ConcurrentDictionary<(int, int, int), HashSet<PeerConnection>>());
        var peers = blockRequests.GetOrAdd((pieceIndex, begin, length), _ => new HashSet<PeerConnection>());
        lock (peers)
        {
            peers.Add(peer);
        }
    }

    public bool HasRequested(int torrentId, int pieceIndex, int begin, int length, PeerConnection peer)
    {
        if (peer == null)
        {
            return false;
        }

        if (_torrentRequests.TryGetValue(torrentId, out var blockRequests))
        {
            if (blockRequests.TryGetValue((pieceIndex, begin, length), out var peers))
            {
                lock (peers)
                {
                    return peers.Contains(peer);
                }
            }

            foreach (var kvp in blockRequests)
            {
                if (kvp.Key.PieceIndex == pieceIndex && kvp.Key.Begin == begin)
                {
                    lock (kvp.Value)
                    {
                        if (kvp.Value.Contains(peer))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    public List<PeerConnection> OnBlockReceived(int torrentId, int pieceIndex, int begin, int length, PeerConnection sourcePeer)
    {
        var otherPeers = new List<PeerConnection>();

        if (_torrentRequests.TryGetValue(torrentId, out var blockRequests))
        {
            var key = (pieceIndex, begin, length);
            if (blockRequests.TryRemove(key, out var peers))
            {
                lock (peers)
                {
                    foreach (var peer in peers)
                    {
                        if (peer != sourcePeer && peer != null && !otherPeers.Contains(peer))
                        {
                            otherPeers.Add(peer);
                        }
                    }
                }
            }
            else
            {
                var matchingKeys = blockRequests.Keys.Where(k => k.PieceIndex == pieceIndex && k.Begin == begin).ToList();
                foreach (var k in matchingKeys)
                {
                    if (blockRequests.TryRemove(k, out var matchedPeers))
                    {
                        lock (matchedPeers)
                        {
                            foreach (var peer in matchedPeers)
                            {
                                if (peer != sourcePeer && peer != null && !otherPeers.Contains(peer))
                                {
                                    otherPeers.Add(peer);
                                }
                            }
                        }
                    }
                }
            }
        }

        return otherPeers;
    }

    public void ClearTorrent(int torrentId)
    {
        _torrentRequests.TryRemove(torrentId, out _);
    }

    public void UnregisterPeer(PeerConnection peer)
    {
        if (peer == null)
        {
            return;
        }

        foreach (var torrentKvp in _torrentRequests.Values)
        {
            foreach (var peers in torrentKvp.Values)
            {
                lock (peers)
                {
                    peers.Remove(peer);
                }
            }
        }
    }
}
