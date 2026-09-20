using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Seeding;

public class SuperSeedingTracker : ISuperSeedingTracker
{
    private readonly object _lock = new();
    private readonly HashSet<string> _selfishPeers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _activeAssignedPieceByPeer = new(StringComparer.OrdinalIgnoreCase);
    private SuperSeedingPieceInfo[] _pieces;

    public int PieceCount { get; private set; }
    public TimeSpan PropagationTimeout { get; set; } = TimeSpan.FromSeconds(90);

    public int PropagatedCount
    {
        get
        {
            lock (_lock)
            {
                return _pieces?.Count(p => p.State == SuperSeedingPieceState.Propagated) ?? 0;
            }
        }
    }

    public int UploadedCount
    {
        get
        {
            lock (_lock)
            {
                return _pieces?.Count(p => p.State == SuperSeedingPieceState.Uploaded) ?? 0;
            }
        }
    }

    public int OfferedCount
    {
        get
        {
            lock (_lock)
            {
                return _pieces?.Count(p => p.State == SuperSeedingPieceState.Offered) ?? 0;
            }
        }
    }

    public int UnseededCount
    {
        get
        {
            lock (_lock)
            {
                return _pieces?.Count(p => p.State == SuperSeedingPieceState.Unseeded) ?? 0;
            }
        }
    }

    public SuperSeedingTracker()
        : this(0, TimeSpan.FromSeconds(90))
    {
    }

    public SuperSeedingTracker(int pieceCount, double propagationTimeoutSeconds = 90)
        : this(pieceCount, TimeSpan.FromSeconds(propagationTimeoutSeconds))
    {
    }

    public SuperSeedingTracker(int pieceCount, TimeSpan propagationTimeout)
    {
        PropagationTimeout = propagationTimeout;
        Initialize(pieceCount);
    }

    public void Initialize(int pieceCount)
    {
        lock (_lock)
        {
            PieceCount = Math.Max(0, pieceCount);
            _pieces = new SuperSeedingPieceInfo[PieceCount];
            for (var i = 0; i < PieceCount; i++)
            {
                _pieces[i] = new SuperSeedingPieceInfo
                {
                    PieceIndex = i,
                    State = SuperSeedingPieceState.Unseeded
                };
            }

            _selfishPeers.Clear();
            _activeAssignedPieceByPeer.Clear();
        }
    }

    public SuperSeedingPieceState GetPieceState(int pieceIndex)
    {
        lock (_lock)
        {
            if (_pieces == null || pieceIndex < 0 || pieceIndex >= PieceCount)
            {
                return SuperSeedingPieceState.Unseeded;
            }

            return _pieces[pieceIndex].State;
        }
    }

    public SuperSeedingPieceInfo GetPieceInfo(int pieceIndex)
    {
        lock (_lock)
        {
            if (_pieces == null || pieceIndex < 0 || pieceIndex >= PieceCount)
            {
                return null;
            }

            var p = _pieces[pieceIndex];
            return new SuperSeedingPieceInfo
            {
                PieceIndex = p.PieceIndex,
                State = p.State,
                AssignedPeerId = p.AssignedPeerId,
                UploadedAt = p.UploadedAt
            };
        }
    }

    public bool IsPeerEligible(string peerId)
    {
        if (string.IsNullOrEmpty(peerId))
        {
            return false;
        }

        lock (_lock)
        {
            if (_selfishPeers.Contains(peerId))
            {
                return false;
            }

            if (_activeAssignedPieceByPeer.TryGetValue(peerId, out var assignedPiece))
            {
                if (assignedPiece >= 0 && assignedPiece < PieceCount)
                {
                    // Eligible only if the previously assigned piece has been marked Propagated
                    if (_pieces[assignedPiece].State == SuperSeedingPieceState.Propagated)
                    {
                        _activeAssignedPieceByPeer.Remove(peerId);
                        return true;
                    }

                    return false;
                }

                _activeAssignedPieceByPeer.Remove(peerId);
            }

            return true;
        }
    }

    public bool IsPeerSelfish(string peerId)
    {
        if (string.IsNullOrEmpty(peerId))
        {
            return false;
        }

        lock (_lock)
        {
            return _selfishPeers.Contains(peerId);
        }
    }

    public void MarkPeerSelfish(string peerId)
    {
        if (string.IsNullOrEmpty(peerId))
        {
            return;
        }

        lock (_lock)
        {
            _selfishPeers.Add(peerId);
        }
    }

    public void UnmarkPeerSelfish(string peerId)
    {
        if (string.IsNullOrEmpty(peerId))
        {
            return;
        }

        lock (_lock)
        {
            _selfishPeers.Remove(peerId);
        }
    }

    public bool TryAllocatePiece(string peerId, bool[] peerPieces, out int pieceIndex)
    {
        pieceIndex = -1;
        if (string.IsNullOrEmpty(peerId) || PieceCount <= 0)
        {
            return false;
        }

        lock (_lock)
        {
            if (!IsPeerEligible(peerId))
            {
                return false;
            }

            // Guaranteed 1.0 Ratio Full Seeding:
            // Ensure every piece in the torrent is seeded to at least one peer before any piece is re-uploaded.
            // 1. First, search for Unseeded pieces that this peer does not already have.
            for (var i = 0; i < PieceCount; i++)
            {
                if (peerPieces != null && i < peerPieces.Length && peerPieces[i])
                {
                    continue;
                }

                if (_pieces[i].State == SuperSeedingPieceState.Unseeded)
                {
                    pieceIndex = i;
                    break;
                }
            }

            // 2. If no Unseeded pieces exist, check if all pieces are accounted for.
            // If all pieces have been seeded at least once (i.e. every piece is Offered, Uploaded, or Propagated),
            // and more uploads are allowed, choose a piece the peer doesn't have that is already Propagated.
            if (pieceIndex == -1)
            {
                var anyUnseededInTorrent = _pieces.Any(p => p.State == SuperSeedingPieceState.Unseeded);
                if (!anyUnseededInTorrent)
                {
                    for (var i = 0; i < PieceCount; i++)
                    {
                        if (peerPieces != null && i < peerPieces.Length && peerPieces[i])
                        {
                            continue;
                        }

                        // Avoid pieces currently Offered or Uploaded to another active peer
                        if (_pieces[i].State == SuperSeedingPieceState.Propagated)
                        {
                            pieceIndex = i;
                            break;
                        }
                    }
                }
            }

            if (pieceIndex != -1)
            {
                _pieces[pieceIndex].State = SuperSeedingPieceState.Offered;
                _pieces[pieceIndex].AssignedPeerId = peerId;
                _pieces[pieceIndex].UploadedAt = null;
                _activeAssignedPieceByPeer[peerId] = pieceIndex;
                return true;
            }

            return false;
        }
    }

    public bool RecordPieceOffered(string peerId, int pieceIndex)
    {
        if (string.IsNullOrEmpty(peerId) || pieceIndex < 0 || pieceIndex >= PieceCount)
        {
            return false;
        }

        lock (_lock)
        {
            _pieces[pieceIndex].State = SuperSeedingPieceState.Offered;
            _pieces[pieceIndex].AssignedPeerId = peerId;
            _pieces[pieceIndex].UploadedAt = null;
            _activeAssignedPieceByPeer[peerId] = pieceIndex;
            return true;
        }
    }

    public bool RecordPieceUploaded(string peerId, int pieceIndex, DateTime? timestamp = null)
    {
        if (string.IsNullOrEmpty(peerId) || pieceIndex < 0 || pieceIndex >= PieceCount)
        {
            return false;
        }

        lock (_lock)
        {
            var piece = _pieces[pieceIndex];
            if (piece.State == SuperSeedingPieceState.Propagated)
            {
                return false;
            }

            piece.State = SuperSeedingPieceState.Uploaded;
            piece.AssignedPeerId = peerId;
            piece.UploadedAt = timestamp ?? DateTime.UtcNow;
            _activeAssignedPieceByPeer[peerId] = pieceIndex;
            return true;
        }
    }

    public PiecePropagationResult RecordPieceHave(string peerId, int pieceIndex)
    {
        if (string.IsNullOrEmpty(peerId) || pieceIndex < 0 || pieceIndex >= PieceCount)
        {
            return new PiecePropagationResult { PieceIndex = pieceIndex, NewlyPropagated = false };
        }

        lock (_lock)
        {
            var piece = _pieces[pieceIndex];

            // If already propagated, nothing further to change
            if (piece.State == SuperSeedingPieceState.Propagated)
            {
                return new PiecePropagationResult { PieceIndex = pieceIndex, NewlyPropagated = false };
            }

            // Case A: The peer that was assigned this piece announces HAVE
            if (string.Equals(piece.AssignedPeerId, peerId, StringComparison.OrdinalIgnoreCase))
            {
                // Peer confirms receipt of piece. It transitions from Offered to Uploaded.
                if (piece.State == SuperSeedingPieceState.Offered)
                {
                    piece.State = SuperSeedingPieceState.Uploaded;
                    piece.UploadedAt = DateTime.UtcNow;
                }

                // Not yet propagated: piece was received by assigned peer, but not yet seen from any 3rd-party peer
                return new PiecePropagationResult { PieceIndex = pieceIndex, NewlyPropagated = false };
            }

            // Case B: Another peer (B != A) announces HAVE for this piece!
            // This proves the piece was shared into the swarm!
            var freedPeerId = piece.AssignedPeerId;
            piece.State = SuperSeedingPieceState.Propagated;

            if (!string.IsNullOrEmpty(freedPeerId) &&
                _activeAssignedPieceByPeer.TryGetValue(freedPeerId, out var activePiece) &&
                activePiece == pieceIndex)
            {
                _activeAssignedPieceByPeer.Remove(freedPeerId);
            }

            return new PiecePropagationResult
            {
                PieceIndex = pieceIndex,
                FreedPeerId = freedPeerId,
                NewlyPropagated = true
            };
        }
    }

    public List<PiecePropagationResult> RecordPeerBitfield(string peerId, bool[] bitfield)
    {
        var results = new List<PiecePropagationResult>();
        if (string.IsNullOrEmpty(peerId) || bitfield == null || PieceCount <= 0)
        {
            return results;
        }

        lock (_lock)
        {
            var max = Math.Min(PieceCount, bitfield.Length);
            for (var i = 0; i < max; i++)
            {
                if (bitfield[i])
                {
                    var result = RecordPieceHave(peerId, i);
                    if (result.NewlyPropagated)
                    {
                        results.Add(result);
                    }
                }
            }
        }

        return results;
    }

    public List<SelfishLeecherResult> CheckTimeouts(DateTime? now = null)
    {
        var currentTime = now ?? DateTime.UtcNow;
        var timeouts = new List<SelfishLeecherResult>();

        lock (_lock)
        {
            if (_pieces == null || PieceCount <= 0)
            {
                return timeouts;
            }

            for (var i = 0; i < PieceCount; i++)
            {
                var piece = _pieces[i];
                if (piece.State == SuperSeedingPieceState.Uploaded &&
                    piece.UploadedAt.HasValue &&
                    !string.IsNullOrEmpty(piece.AssignedPeerId))
                {
                    var heldDuration = currentTime - piece.UploadedAt.Value;
                    if (heldDuration >= PropagationTimeout)
                    {
                        var selfishPeerId = piece.AssignedPeerId;
                        _selfishPeers.Add(selfishPeerId);
                        _activeAssignedPieceByPeer.Remove(selfishPeerId);

                        timeouts.Add(new SelfishLeecherResult
                        {
                            PeerId = selfishPeerId,
                            PieceIndex = i,
                            HeldDuration = heldDuration
                        });

                        // Reset piece so another peer can receive it
                        piece.State = SuperSeedingPieceState.Unseeded;
                        piece.AssignedPeerId = null;
                        piece.UploadedAt = null;
                    }
                }
            }
        }

        return timeouts;
    }

    public void OnPeerDisconnected(string peerId)
    {
        if (string.IsNullOrEmpty(peerId))
        {
            return;
        }

        lock (_lock)
        {
            if (_activeAssignedPieceByPeer.TryGetValue(peerId, out var pieceIndex))
            {
                _activeAssignedPieceByPeer.Remove(peerId);
                if (pieceIndex >= 0 && pieceIndex < PieceCount)
                {
                    var piece = _pieces[pieceIndex];
                    if (piece.State != SuperSeedingPieceState.Propagated)
                    {
                        piece.State = SuperSeedingPieceState.Unseeded;
                        piece.AssignedPeerId = null;
                        piece.UploadedAt = null;
                    }
                }
            }
        }
    }

    public bool ResetPiece(int pieceIndex)
    {
        lock (_lock)
        {
            if (_pieces == null || pieceIndex < 0 || pieceIndex >= PieceCount)
            {
                return false;
            }

            var piece = _pieces[pieceIndex];
            if (!string.IsNullOrEmpty(piece.AssignedPeerId))
            {
                _activeAssignedPieceByPeer.Remove(piece.AssignedPeerId);
            }

            piece.State = SuperSeedingPieceState.Unseeded;
            piece.AssignedPeerId = null;
            piece.UploadedAt = null;
            return true;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            Initialize(PieceCount);
        }
    }
}
