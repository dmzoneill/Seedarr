using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers.SuperSeeding;

public class SuperSeedingService : ISuperSeedingService
{
    private readonly ITorrentService _torrentService;
    private readonly IConnectionManager _connectionManager;
    private readonly ITorrentEventLogService _eventLogService;
    private readonly IFastExtensionHandler _fastExtensionHandler;
    private readonly IEventAggregator _eventAggregator;
    private readonly ISuperSeedingTracker _defaultTracker;
    private readonly ConcurrentDictionary<string, ISuperSeedingTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger _logger;

    public SuperSeedingService(
        ITorrentService torrentService = null,
        IConnectionManager connectionManager = null,
        ITorrentEventLogService eventLogService = null,
        IFastExtensionHandler fastExtensionHandler = null,
        IEventAggregator eventAggregator = null,
        ISuperSeedingTracker defaultTracker = null)
    {
        _torrentService = torrentService;
        _connectionManager = connectionManager;
        _eventLogService = eventLogService;
        _fastExtensionHandler = fastExtensionHandler;
        _eventAggregator = eventAggregator;
        _defaultTracker = defaultTracker;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public ISuperSeedingTracker GetOrCreateTracker(Torrent torrent)
    {
        if (_defaultTracker != null)
        {
            return _defaultTracker;
        }

        if (torrent == null || string.IsNullOrEmpty(torrent.InfoHash))
        {
            return null;
        }

        return _trackers.GetOrAdd(torrent.InfoHash, _ => new Seeding.SuperSeedingTracker(torrent.PieceCount));
    }

    public ISuperSeedingTracker GetTracker(string infoHash, int pieceCount = 0)
    {
        if (_defaultTracker != null)
        {
            return _defaultTracker;
        }

        if (string.IsNullOrEmpty(infoHash))
        {
            return null;
        }

        return _trackers.GetOrAdd(infoHash, _ =>
        {
            var count = pieceCount > 0 ? pieceCount : (_torrentService?.GetByInfoHash(infoHash)?.PieceCount ?? 0);
            return new Seeding.SuperSeedingTracker(count);
        });
    }

    public void SendInitialAvailability(PeerConnection connection, Torrent torrent)
    {
        if (connection == null || torrent == null || torrent.PieceCount <= 0)
        {
            return;
        }

        var byteCount = (torrent.PieceCount + 7) / 8;
        var emptyBitfield = new byte[byteCount];

        if (_fastExtensionHandler != null && (connection.SupportsFastExtension || _fastExtensionHandler.IsFastPeer(connection)))
        {
            _fastExtensionHandler.SendHaveAllOrBitfield(connection, torrent.PieceCount, hasAll: false, hasNone: true, bitfield: emptyBitfield);
        }
        else if (connection.SupportsFastExtension)
        {
            connection.SendMessage(new PeerMessage { Type = PeerMessageType.HaveNone, Payload = Array.Empty<byte>() });
        }
        else
        {
            connection.SendBitfield(emptyBitfield);
        }
    }

    public bool AllocateAndRevealPiece(PeerConnection connection, Torrent torrent)
    {
        if (torrent == null || !torrent.SuperSeeding || torrent.PieceCount <= 0 || connection == null)
        {
            return false;
        }

        if (connection.AmChoking)
        {
            return false;
        }

        var tracker = GetOrCreateTracker(torrent);
        if (tracker == null)
        {
            return false;
        }

        var peerKey = GetPeerKey(connection);

        // BEP 16: Only reveal at most one piece at a time per peer.
        // Only offer a new unseeded piece to Peer A after Peer A has shared its previous piece, or when Peer A has no pending pieces.
        if (connection.AssignedSuperSeedingPiece.HasValue)
        {
            var currentState = tracker.GetPieceState(connection.AssignedSuperSeedingPiece.Value);
            if (currentState != SuperSeedingPieceState.Propagated)
            {
                return false;
            }
        }

        if (!tracker.IsPeerEligible(peerKey))
        {
            return false;
        }

        if (!tracker.TryAllocatePiece(peerKey, connection.PeerPieces, out var chosenPiece))
        {
            return false;
        }

        connection.AssignedSuperSeedingPiece = chosenPiece;
        connection.AssignedPieceBytesUploaded = 0;
        connection.SendHave(chosenPiece);

        _logger.Debug(
            "Super-seeding: revealed piece {0} to peer {1}:{2} on torrent {3}",
            chosenPiece,
            connection.RemoteIp,
            connection.RemotePort,
            torrent.Name ?? torrent.InfoHash);

        return true;
    }

    public bool IsRequestAllowed(PeerConnection connection, Torrent torrent, int pieceIndex, byte[] requestPayload = null)
    {
        if (torrent == null || !torrent.SuperSeeding)
        {
            return true;
        }

        if (connection == null)
        {
            return false;
        }

        if (connection.AssignedSuperSeedingPiece == null || connection.AssignedSuperSeedingPiece.Value != pieceIndex)
        {
            _logger.Debug(
                "Super-seeding active: rejecting request for piece {0} (assigned: {1}) from peer {2}:{3}",
                pieceIndex,
                connection.AssignedSuperSeedingPiece,
                connection.RemoteIp,
                connection.RemotePort);

            if (_fastExtensionHandler != null && (connection.SupportsFastExtension || _fastExtensionHandler.IsFastPeer(connection)))
            {
                var rejectMsg = requestPayload != null
                    ? _fastExtensionHandler.BuildRejectForRequest(requestPayload)
                    : _fastExtensionHandler.SerializeRejectRequest(pieceIndex, 0, 16384);
                if (rejectMsg != null)
                {
                    connection.SendMessage(rejectMsg);
                }
            }
            else if (connection.SupportsFastExtension && requestPayload != null)
            {
                connection.SendMessage(new PeerMessage
                {
                    Type = PeerMessageType.RejectRequest,
                    Payload = requestPayload,
                    PayloadLength = requestPayload.Length
                });
            }

            return false;
        }

        return true;
    }

    public void HandleBlockUploaded(PeerConnection connection, Torrent torrent, int pieceIndex, int bytesUploaded)
    {
        if (connection == null || torrent == null || !torrent.SuperSeeding || bytesUploaded <= 0)
        {
            return;
        }

        connection.AssignedPieceBytesUploaded += bytesUploaded;
        var pieceSize = (torrent.PieceLength > 0 && torrent.TotalSize > 0 && pieceIndex == torrent.PieceCount - 1)
            ? (int)(torrent.TotalSize - ((long)pieceIndex * torrent.PieceLength))
            : (torrent.PieceLength > 0 ? torrent.PieceLength : 16384);

        if (connection.AssignedPieceBytesUploaded >= pieceSize)
        {
            var tracker = GetOrCreateTracker(torrent);
            tracker?.RecordPieceUploaded(GetPeerKey(connection), pieceIndex);
        }
    }

    public void OnPeerHave(PeerConnection connection, Torrent torrent, int pieceIndex)
    {
        if (connection == null || torrent == null || !torrent.SuperSeeding || torrent.PieceCount <= 0)
        {
            return;
        }

        // Check automated exit criteria first
        if (CheckExitCriteria(torrent, connection))
        {
            return;
        }

        var tracker = GetOrCreateTracker(torrent);
        var peerKey = GetPeerKey(connection);
        if (tracker != null)
        {
            var result = tracker.RecordPieceHave(peerKey, pieceIndex);
            if (result.NewlyPropagated && !string.IsNullOrEmpty(result.FreedPeerId))
            {
                var peers = _connectionManager?.GetConnections(torrent.InfoHash);
                var freedPeer = peers?.FirstOrDefault(p => p != null && GetPeerKey(p) == result.FreedPeerId);
                if (freedPeer != null && !freedPeer.AmChoking)
                {
                    AllocateAndRevealPiece(freedPeer, torrent);
                }
            }
        }

        if (!connection.AmChoking && connection.AssignedSuperSeedingPiece == null)
        {
            AllocateAndRevealPiece(connection, torrent);
        }
    }

    public void OnPeerBitfield(PeerConnection connection, Torrent torrent, bool[] bitfield)
    {
        if (connection == null || torrent == null || !torrent.SuperSeeding || torrent.PieceCount <= 0 || bitfield == null)
        {
            return;
        }

        // Check automated exit criteria first
        if (CheckExitCriteria(torrent, connection))
        {
            return;
        }

        var tracker = GetOrCreateTracker(torrent);
        var peerKey = GetPeerKey(connection);
        if (tracker != null)
        {
            var propagations = tracker.RecordPeerBitfield(peerKey, bitfield);
            if (propagations != null && propagations.Count > 0)
            {
                var peers = _connectionManager?.GetConnections(torrent.InfoHash);
                foreach (var prop in propagations)
                {
                    if (prop.NewlyPropagated && !string.IsNullOrEmpty(prop.FreedPeerId))
                    {
                        var freedPeer = peers?.FirstOrDefault(p => p != null && GetPeerKey(p) == prop.FreedPeerId);
                        if (freedPeer != null && !freedPeer.AmChoking)
                        {
                            AllocateAndRevealPiece(freedPeer, torrent);
                        }
                    }
                }
            }
        }

        if (!connection.AmChoking && connection.AssignedSuperSeedingPiece == null)
        {
            AllocateAndRevealPiece(connection, torrent);
        }
    }

    public void OnPeerHaveAll(PeerConnection connection, Torrent torrent)
    {
        if (connection == null || torrent == null || !torrent.SuperSeeding)
        {
            return;
        }

        ExitSuperSeeding(torrent, "secondary seed joined");
    }

    public List<SelfishLeecherResult> CheckTimeouts(Torrent torrent = null, DateTime? now = null)
    {
        var allTimeouts = new List<SelfishLeecherResult>();

        if (torrent != null)
        {
            if (!torrent.SuperSeeding || string.IsNullOrEmpty(torrent.InfoHash))
            {
                return allTimeouts;
            }

            var tracker = GetOrCreateTracker(torrent);
            if (tracker != null)
            {
                var timeouts = tracker.CheckTimeouts(now);
                if (timeouts != null && timeouts.Count > 0)
                {
                    ProcessSelfishLeechers(timeouts, torrent);
                    allTimeouts.AddRange(timeouts);
                }
            }
        }
        else
        {
            foreach (var kvp in _trackers)
            {
                var infoHash = kvp.Key;
                var tracker = kvp.Value;
                var cachedTorrent = _torrentService?.GetByInfoHash(infoHash);
                if (cachedTorrent != null && cachedTorrent.SuperSeeding)
                {
                    var timeouts = tracker.CheckTimeouts(now);
                    if (timeouts != null && timeouts.Count > 0)
                    {
                        ProcessSelfishLeechers(timeouts, cachedTorrent);
                        allTimeouts.AddRange(timeouts);
                    }
                }
            }
        }

        return allTimeouts;
    }

    public bool CheckExitCriteria(Torrent torrent, PeerConnection triggerPeer = null)
    {
        if (torrent == null || !torrent.SuperSeeding || string.IsNullOrEmpty(torrent.InfoHash))
        {
            return false;
        }

        if (triggerPeer != null && (triggerPeer.IsSeed || triggerPeer.Progress >= 1.0 || (torrent.PieceCount > 0 && triggerPeer.HaveCount == torrent.PieceCount)))
        {
            ExitSuperSeeding(torrent, "secondary seed joined");
            return true;
        }

        var peers = _connectionManager?.GetConnections(torrent.InfoHash);
        if (peers != null && peers.Count > 0)
        {
            if (peers.Any(p => p != null && (p.IsSeed || p.Progress >= 1.0 || (torrent.PieceCount > 0 && p.HaveCount == torrent.PieceCount))))
            {
                ExitSuperSeeding(torrent, "secondary seed joined", peers);
                return true;
            }

            if (torrent.PieceCount > 0)
            {
                var swarmAvailability = SwarmAvailabilityService.CalculateCumulativeAvailability(peers, torrent.PieceCount);
                if (swarmAvailability >= 1.0)
                {
                    ExitSuperSeeding(torrent, "swarm availability >= 1.0", peers);
                    return true;
                }
            }
        }

        return false;
    }

    public void ExitSuperSeeding(Torrent torrent, string reason, List<PeerConnection> connectedPeers = null)
    {
        if (torrent == null || !torrent.SuperSeeding)
        {
            return;
        }

        torrent.SuperSeeding = false;

        try
        {
            _torrentService?.Update(torrent);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error updating torrent {0} after exiting super-seeding", torrent.Id);
        }

        var message = $"Super-seeding completed: {reason}";
        _eventLogService?.Info(torrent.Id, "SuperSeeding", message);
        _logger.Info("Torrent '{0}' (Id: {1}) - {2}", torrent.Name, torrent.Id, message);

        _eventAggregator?.PublishEvent(new SuperSeedingExitedEvent(torrent, reason));
        _eventAggregator?.PublishEvent(new TorrentUpdatedEvent(torrent));

        connectedPeers ??= _connectionManager?.GetConnections(torrent.InfoHash);
        if (connectedPeers != null && torrent.PieceCount > 0)
        {
            foreach (var peer in connectedPeers)
            {
                try
                {
                    if (peer != null)
                    {
                        peer.AssignedSuperSeedingPiece = null;
                        peer.AssignedPieceBytesUploaded = 0;

                        if (_fastExtensionHandler != null && (peer.SupportsFastExtension || _fastExtensionHandler.IsFastPeer(peer)))
                        {
                            peer.SendMessage(_fastExtensionHandler.SerializeHaveAll());
                        }
                        else if (peer.SupportsFastExtension)
                        {
                            peer.SendMessage(new PeerMessage { Type = PeerMessageType.HaveAll, Payload = Array.Empty<byte>() });
                        }
                        else
                        {
                            peer.SendBitfield(torrent.PieceCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error sending full availability to peer {0}:{1} after exiting super-seeding", peer?.RemoteIp, peer?.RemotePort);
                }
            }
        }
    }

    public void OnPeerDisconnected(PeerConnection connection, Torrent torrent = null)
    {
        if (connection == null)
        {
            return;
        }

        var hash = torrent?.InfoHash ?? connection.InfoHash;
        if (!string.IsNullOrEmpty(hash))
        {
            var tracker = GetTracker(hash);
            tracker?.OnPeerDisconnected(GetPeerKey(connection));
        }

        connection.AssignedSuperSeedingPiece = null;
        connection.AssignedPieceBytesUploaded = 0;
    }

    private void ProcessSelfishLeechers(List<SelfishLeecherResult> timeouts, Torrent torrent)
    {
        var peers = _connectionManager?.GetConnections(torrent.InfoHash);
        foreach (var timeout in timeouts)
        {
            var peer = peers?.FirstOrDefault(p => p != null && GetPeerKey(p) == timeout.PeerId);
            if (peer != null)
            {
                peer.AmChoking = true;
                peer.AssignedSuperSeedingPiece = null;
                peer.AssignedPieceBytesUploaded = 0;
                try
                {
                    peer.SendMessage(new PeerMessage { Type = PeerMessageType.Choke });
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error sending choke message to selfish peer {0}", timeout.PeerId);
                }

                _logger.Warn(
                    "Super-seeding: choked selfish peer {0} on torrent {1} after holding piece {2} for {3:F1}s without propagation",
                    timeout.PeerId,
                    torrent.Name ?? torrent.InfoHash,
                    timeout.PieceIndex,
                    timeout.HeldDuration.TotalSeconds);
            }

            if (peers != null)
            {
                foreach (var otherPeer in peers)
                {
                    if (otherPeer != null && otherPeer != peer && !otherPeer.AmChoking && otherPeer.AssignedSuperSeedingPiece == null)
                    {
                        if (AllocateAndRevealPiece(otherPeer, torrent))
                        {
                            break;
                        }
                    }
                }
            }
        }
    }

    private static string GetPeerKey(PeerConnection connection)
    {
        if (connection == null)
        {
            return string.Empty;
        }

        return !string.IsNullOrEmpty(connection.PeerId)
            ? connection.PeerId
            : $"{connection.RemoteIp}:{connection.RemotePort}";
    }
}
