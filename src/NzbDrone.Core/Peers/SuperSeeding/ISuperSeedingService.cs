using System;
using System.Collections.Generic;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers.SuperSeeding;

public interface ISuperSeedingService
{
    ISuperSeedingTracker GetOrCreateTracker(Torrent torrent);
    ISuperSeedingTracker GetTracker(string infoHash, int pieceCount = 0);

    void SendInitialAvailability(PeerConnection connection, Torrent torrent);
    bool AllocateAndRevealPiece(PeerConnection connection, Torrent torrent);
    bool IsRequestAllowed(PeerConnection connection, Torrent torrent, int pieceIndex, byte[] requestPayload = null);
    void HandleBlockUploaded(PeerConnection connection, Torrent torrent, int pieceIndex, int bytesUploaded);
    void OnPeerHave(PeerConnection connection, Torrent torrent, int pieceIndex);
    void OnPeerBitfield(PeerConnection connection, Torrent torrent, bool[] bitfield);
    void OnPeerHaveAll(PeerConnection connection, Torrent torrent);
    List<SelfishLeecherResult> CheckTimeouts(Torrent torrent = null, DateTime? now = null);
    bool CheckExitCriteria(Torrent torrent, PeerConnection triggerPeer = null);
    void ExitSuperSeeding(Torrent torrent, string reason, List<PeerConnection> connectedPeers = null);
    void OnPeerDisconnected(PeerConnection connection, Torrent torrent = null);
}
