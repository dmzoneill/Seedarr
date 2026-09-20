using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.Seeding;

namespace NzbDrone.Core.Test.Seeding;

[TestFixture]
public class SuperSeedingTrackerTests
{
    [Test]
    public void PropagationLifecycle_TransitionsThroughAllStates()
    {
        // Arrange: 5-piece tracker
        var tracker = new SuperSeedingTracker(5, propagationTimeoutSeconds: 90);

        // 1. Initial state: all pieces are Unseeded
        for (var i = 0; i < 5; i++)
        {
            Assert.That(tracker.GetPieceState(i), Is.EqualTo(SuperSeedingPieceState.Unseeded));
        }

        Assert.That(tracker.UnseededCount, Is.EqualTo(5));
        Assert.That(tracker.OfferedCount, Is.EqualTo(0));
        Assert.That(tracker.UploadedCount, Is.EqualTo(0));
        Assert.That(tracker.PropagatedCount, Is.EqualTo(0));

        // 2. Allocate piece to Peer A -> state becomes Offered
        var allocated = tracker.TryAllocatePiece("peerA", null, out var piece0);
        Assert.That(allocated, Is.True);
        Assert.That(piece0, Is.EqualTo(0));
        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Offered));
        Assert.That(tracker.GetPieceInfo(0)?.AssignedPeerId, Is.EqualTo("peerA"));
        Assert.That(tracker.OfferedCount, Is.EqualTo(1));
        Assert.That(tracker.UnseededCount, Is.EqualTo(4));

        // 3. Piece completely uploaded by Peer A -> state becomes Uploaded
        var uploadTime = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var uploadRecorded = tracker.RecordPieceUploaded("peerA", 0, uploadTime);
        Assert.That(uploadRecorded, Is.True);
        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Uploaded));
        Assert.That(tracker.GetPieceInfo(0)?.UploadedAt, Is.EqualTo(uploadTime));
        Assert.That(tracker.UploadedCount, Is.EqualTo(1));
        Assert.That(tracker.OfferedCount, Is.EqualTo(0));

        // 4. Third-party Peer B announces HAVE(0) -> state becomes Propagated
        var propResult = tracker.RecordPieceHave("peerB", 0);
        Assert.That(propResult.NewlyPropagated, Is.True);
        Assert.That(propResult.FreedPeerId, Is.EqualTo("peerA"));
        Assert.That(propResult.PieceIndex, Is.EqualTo(0));
        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Propagated));
        Assert.That(tracker.PropagatedCount, Is.EqualTo(1));
        Assert.That(tracker.UploadedCount, Is.EqualTo(0));
    }

    [Test]
    public void PeerEligibility_PeerCannotGetSecondPieceUntilFirstPieceObservedFromAnotherPeer()
    {
        var tracker = new SuperSeedingTracker(10, propagationTimeoutSeconds: 90);

        // Allocate first piece to Peer A
        Assert.That(tracker.IsPeerEligible("peerA"), Is.True);
        var allocatedA = tracker.TryAllocatePiece("peerA", null, out var pieceA);
        Assert.That(allocatedA, Is.True);
        Assert.That(pieceA, Is.EqualTo(0));

        // Peer A downloads piece 0 and sends HAVE(0)
        var aHave = tracker.RecordPieceHave("peerA", 0);
        Assert.That(aHave.NewlyPropagated, Is.False);
        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Uploaded));

        // Peer A is NOT eligible for a new piece because piece 0 has not propagated
        Assert.That(tracker.IsPeerEligible("peerA"), Is.False);
        var secondAlloc = tracker.TryAllocatePiece("peerA", null, out _);
        Assert.That(secondAlloc, Is.False);

        // Peer B connects and receives piece 1
        Assert.That(tracker.IsPeerEligible("peerB"), Is.True);
        var allocatedB = tracker.TryAllocatePiece("peerB", null, out var pieceB);
        Assert.That(allocatedB, Is.True);
        Assert.That(pieceB, Is.EqualTo(1));

        // Peer B receives piece 0 from Peer A and announces HAVE(0) to seeder
        var bHave0 = tracker.RecordPieceHave("peerB", 0);
        Assert.That(bHave0.NewlyPropagated, Is.True);
        Assert.That(bHave0.FreedPeerId, Is.EqualTo("peerA"));
        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Propagated));

        // Peer A is now eligible for its second piece!
        Assert.That(tracker.IsPeerEligible("peerA"), Is.True);
        var thirdAlloc = tracker.TryAllocatePiece("peerA", null, out var nextPieceA);
        Assert.That(thirdAlloc, Is.True);
        Assert.That(nextPieceA, Is.EqualTo(2));
    }

    [Test]
    public void SelfishLeecherTimeout_TriggersAfterConfiguredDuration_ResetsPieceAndMarksPeerSelfish()
    {
        var tracker = new SuperSeedingTracker(5, propagationTimeoutSeconds: 90);
        var startTime = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

        // Allocate piece 0 to Peer A and mark uploaded
        tracker.TryAllocatePiece("peerA", null, out var piece0);
        tracker.RecordPieceUploaded("peerA", piece0, startTime);

        // 89 seconds elapsed: should not trigger timeout
        var timeouts89 = tracker.CheckTimeouts(startTime.AddSeconds(89));
        Assert.That(timeouts89, Is.Empty);
        Assert.That(tracker.IsPeerSelfish("peerA"), Is.False);
        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Uploaded));

        // 90 seconds elapsed: triggers timeout!
        var timeouts90 = tracker.CheckTimeouts(startTime.AddSeconds(90));
        Assert.That(timeouts90.Count, Is.EqualTo(1));
        Assert.That(timeouts90[0].PeerId, Is.EqualTo("peerA"));
        Assert.That(timeouts90[0].PieceIndex, Is.EqualTo(0));
        Assert.That(timeouts90[0].HeldDuration, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(90)));

        // Peer A is marked selfish and ineligible
        Assert.That(tracker.IsPeerSelfish("peerA"), Is.True);
        Assert.That(tracker.IsPeerEligible("peerA"), Is.False);

        // Piece 0 was reset to Unseeded
        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Unseeded));

        // Peer B can now be allocated the reset piece 0
        var allocB = tracker.TryAllocatePiece("peerB", null, out var pieceB);
        Assert.That(allocB, Is.True);
        Assert.That(pieceB, Is.EqualTo(0));
    }

    [Test]
    public void GuaranteedOnePointZeroRatio_SeedsAllPiecesBeforeDuplicatingAnyPiece()
    {
        const int pieceCount = 4;
        var tracker = new SuperSeedingTracker(pieceCount, propagationTimeoutSeconds: 90);

        var allocatedPieces = new List<int>();
        var peers = new[] { "peer1", "peer2", "peer3", "peer4" };

        // Allocate one piece to each peer
        foreach (var peer in peers)
        {
            var success = tracker.TryAllocatePiece(peer, null, out var piece);
            Assert.That(success, Is.True);
            allocatedPieces.Add(piece);
        }

        // Every piece 0, 1, 2, 3 must be allocated exactly once without duplication
        Assert.That(allocatedPieces.Distinct().Count(), Is.EqualTo(pieceCount));
        Assert.That(tracker.UnseededCount, Is.EqualTo(0));
        for (var i = 0; i < pieceCount; i++)
        {
            Assert.That(tracker.GetPieceState(i), Is.EqualTo(SuperSeedingPieceState.Offered));
        }

        // None of the peers can get a duplicate piece right now because pieces haven't propagated
        var duplicateAlloc = tracker.TryAllocatePiece("peer1", null, out _);
        Assert.That(duplicateAlloc, Is.False);
    }

    [Test]
    public void RecordPeerBitfield_PropagatesMultiplePiecesFromThirdParty()
    {
        var tracker = new SuperSeedingTracker(5, propagationTimeoutSeconds: 90);

        tracker.TryAllocatePiece("peerA", null, out _); // piece 0
        tracker.TryAllocatePiece("peerB", null, out _); // piece 1
        tracker.TryAllocatePiece("peerC", null, out _); // piece 2

        tracker.RecordPieceUploaded("peerA", 0);
        tracker.RecordPieceUploaded("peerB", 1);
        tracker.RecordPieceUploaded("peerC", 2);

        // Peer D connects and announces bitfield with pieces 0 and 2
        var bitfield = new bool[] { true, false, true, false, false };
        var propagations = tracker.RecordPeerBitfield("peerD", bitfield);

        Assert.That(propagations.Count, Is.EqualTo(2));
        var freed = propagations.Select(p => p.FreedPeerId).ToList();
        Assert.That(freed, Contains.Item("peerA"));
        Assert.That(freed, Contains.Item("peerC"));

        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Propagated));
        Assert.That(tracker.GetPieceState(1), Is.EqualTo(SuperSeedingPieceState.Uploaded));
        Assert.That(tracker.GetPieceState(2), Is.EqualTo(SuperSeedingPieceState.Propagated));

        // Peers A and C are now eligible; Peer B is still waiting
        Assert.That(tracker.IsPeerEligible("peerA"), Is.True);
        Assert.That(tracker.IsPeerEligible("peerB"), Is.False);
        Assert.That(tracker.IsPeerEligible("peerC"), Is.True);
    }

    [Test]
    public void OnPeerDisconnected_ResetsUnpropagatedPieceToUnseeded()
    {
        var tracker = new SuperSeedingTracker(5, propagationTimeoutSeconds: 90);

        tracker.TryAllocatePiece("peerA", null, out var piece);
        Assert.That(piece, Is.EqualTo(0));
        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Offered));

        tracker.OnPeerDisconnected("peerA");

        Assert.That(tracker.GetPieceState(0), Is.EqualTo(SuperSeedingPieceState.Unseeded));

        var reallocated = tracker.TryAllocatePiece("peerB", null, out var pieceB);
        Assert.That(reallocated, Is.True);
        Assert.That(pieceB, Is.EqualTo(0));
    }
}
