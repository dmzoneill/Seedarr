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

    void BroadcastPex(string infoHash = null);

    void BroadcastLtDontHave(string infoHash, int pieceIndex);

    void BroadcastLtDontHave(string infoHash, System.Collections.Generic.IEnumerable<int> pieceIndices);

    byte[] BuildPexMessage(string infoHash);

    System.Collections.Generic.List<Seeding.SelfishLeecherResult> CheckSuperSeedingTimeouts(Torrent torrent = null, System.DateTime? now = null);

    Seeding.ISuperSeedingTracker GetSuperSeedingTracker(string infoHash, int pieceCount = 0);
}
