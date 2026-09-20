using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Seeding;

public enum SuperSeedingPieceState
{
    Unseeded,
    Offered,
    Uploaded,
    Propagated
}

public class SuperSeedingPieceInfo
{
    public int PieceIndex { get; set; }
    public SuperSeedingPieceState State { get; set; } = SuperSeedingPieceState.Unseeded;
    public string AssignedPeerId { get; set; }
    public DateTime? UploadedAt { get; set; }
}

public class SelfishLeecherResult
{
    public string PeerId { get; set; }
    public int PieceIndex { get; set; }
    public TimeSpan HeldDuration { get; set; }
}

public class PiecePropagationResult
{
    public int PieceIndex { get; set; }
    public string FreedPeerId { get; set; }
    public bool NewlyPropagated { get; set; }
}

public interface ISuperSeedingTracker
{
    int PieceCount { get; }
    TimeSpan PropagationTimeout { get; set; }
    int PropagatedCount { get; }
    int UploadedCount { get; }
    int OfferedCount { get; }
    int UnseededCount { get; }

    SuperSeedingPieceState GetPieceState(int pieceIndex);
    SuperSeedingPieceInfo GetPieceInfo(int pieceIndex);

    bool IsPeerEligible(string peerId);
    bool IsPeerSelfish(string peerId);
    void MarkPeerSelfish(string peerId);
    void UnmarkPeerSelfish(string peerId);

    bool TryAllocatePiece(string peerId, bool[] peerPieces, out int pieceIndex);
    bool RecordPieceOffered(string peerId, int pieceIndex);
    bool RecordPieceUploaded(string peerId, int pieceIndex, DateTime? timestamp = null);
    PiecePropagationResult RecordPieceHave(string peerId, int pieceIndex);
    List<PiecePropagationResult> RecordPeerBitfield(string peerId, bool[] bitfield);
    List<SelfishLeecherResult> CheckTimeouts(DateTime? now = null);

    void OnPeerDisconnected(string peerId);
    bool ResetPiece(int pieceIndex);
    void Reset();
}
