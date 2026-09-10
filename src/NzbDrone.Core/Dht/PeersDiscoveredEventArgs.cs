using System;
using System.Collections.Generic;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Dht;

public class PeersDiscoveredEventArgs : EventArgs
{
    public string InfoHash { get; set; }
    public List<TrackerPeer> Peers { get; set; }

    public PeersDiscoveredEventArgs()
    {
    }

    public PeersDiscoveredEventArgs(string infoHash, List<TrackerPeer> peers)
    {
        InfoHash = infoHash;
        Peers = peers;
    }
}
