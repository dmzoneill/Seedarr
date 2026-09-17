using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public interface IPeerServer
{
    bool IsListening { get; }

    bool BindFailed { get; }

    int ListeningPort { get; }

    string BindErrorMessage { get; }

    void StopListening();

    void UpdateLocalInterest(PeerConnection connection, Torrent torrent);

    void OnTorrentCompleted(Torrent torrent);

    void ChokePeers(string infoHash);

    void UnchokePeers(string infoHash);
}
