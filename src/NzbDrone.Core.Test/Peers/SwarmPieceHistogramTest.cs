using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class SwarmPieceHistogramTest
{
    [Test]
    public void IncrementPiece_should_update_availability_and_rarity_buckets()
    {
        var histogram = new SwarmPieceHistogram(5);

        Assert.That(histogram.TotalPieces, Is.EqualTo(5));
        Assert.That(histogram.GetAvailability(2), Is.EqualTo(0));
        Assert.That(histogram.GetBucketPieceCount(0), Is.EqualTo(5));
        Assert.That(histogram.GetPiecesInBucket(0), Contains.Item(2));

        histogram.IncrementPiece(2);

        Assert.That(histogram.GetAvailability(2), Is.EqualTo(1));
        Assert.That(histogram.GetPiecesInBucket(0), Does.Not.Contain(2));
        Assert.That(histogram.GetPiecesInBucket(1), Contains.Item(2));
        Assert.That(histogram.GetBucketPieceCount(0), Is.EqualTo(4));
        Assert.That(histogram.GetBucketPieceCount(1), Is.EqualTo(1));

        histogram.IncrementPiece(2);

        Assert.That(histogram.GetAvailability(2), Is.EqualTo(2));
        Assert.That(histogram.GetPiecesInBucket(1), Does.Not.Contain(2));
        Assert.That(histogram.GetPiecesInBucket(2), Contains.Item(2));

        histogram.IncrementPiece(4);

        Assert.That(histogram.GetAvailability(4), Is.EqualTo(1));
        Assert.That(histogram.GetPiecesInBucket(1), Contains.Item(4));
        Assert.That(histogram.GetBucketPieceCount(1), Is.EqualTo(1));
        Assert.That(histogram.GetBucketPieceCount(2), Is.EqualTo(1));
        Assert.That(histogram.GetBucketPieceCount(0), Is.EqualTo(3));
    }

    [Test]
    public void DecrementPiece_should_update_availability_and_rarity_buckets()
    {
        var histogram = new SwarmPieceHistogram(5);
        histogram.IncrementPiece(1);
        histogram.IncrementPiece(1);

        Assert.That(histogram.GetAvailability(1), Is.EqualTo(2));
        Assert.That(histogram.GetPiecesInBucket(2), Contains.Item(1));

        histogram.DecrementPiece(1);

        Assert.That(histogram.GetAvailability(1), Is.EqualTo(1));
        Assert.That(histogram.GetPiecesInBucket(2), Does.Not.Contain(1));
        Assert.That(histogram.GetPiecesInBucket(1), Contains.Item(1));

        histogram.DecrementPiece(1);

        Assert.That(histogram.GetAvailability(1), Is.EqualTo(0));
        Assert.That(histogram.GetPiecesInBucket(1), Does.Not.Contain(1));
        Assert.That(histogram.GetPiecesInBucket(0), Contains.Item(1));

        // Decrementing when already 0 should be a no-op and avoid underflow
        histogram.DecrementPiece(1);

        Assert.That(histogram.GetAvailability(1), Is.EqualTo(0));
        Assert.That(histogram.GetPiecesInBucket(0), Contains.Item(1));
    }

    [Test]
    public void RegisterPeer_and_UnregisterPeer_updates_availability_and_avoids_ghost_counts()
    {
        var histogram = new SwarmPieceHistogram(5);

        var peer1Pieces = new[] { true, true, true, false, false };
        var peer2Pieces = new[] { false, true, true, true, false };

        histogram.RegisterPeer(peer1Pieces);

        Assert.That(histogram.GetAvailability(0), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(1), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(2), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(3), Is.EqualTo(0));
        Assert.That(histogram.GetAvailability(4), Is.EqualTo(0));

        histogram.RegisterPeer(peer2Pieces);

        Assert.That(histogram.GetAvailability(0), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(1), Is.EqualTo(2));
        Assert.That(histogram.GetAvailability(2), Is.EqualTo(2));
        Assert.That(histogram.GetAvailability(3), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(4), Is.EqualTo(0));

        Assert.That(histogram.GetPiecesInBucket(2), Is.EquivalentTo(new[] { 1, 2 }));
        Assert.That(histogram.GetPiecesInBucket(1), Is.EquivalentTo(new[] { 0, 3 }));
        Assert.That(histogram.GetPiecesInBucket(0), Is.EquivalentTo(new[] { 4 }));

        // Peer 1 disconnects
        histogram.UnregisterPeer(peer1Pieces);

        Assert.That(histogram.GetAvailability(0), Is.EqualTo(0));
        Assert.That(histogram.GetAvailability(1), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(2), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(3), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(4), Is.EqualTo(0));

        Assert.That(histogram.GetPiecesInBucket(2), Is.Empty);
        Assert.That(histogram.GetPiecesInBucket(1), Is.EquivalentTo(new[] { 1, 2, 3 }));
        Assert.That(histogram.GetPiecesInBucket(0), Is.EquivalentTo(new[] { 0, 4 }));

        // Peer 2 disconnects
        histogram.UnregisterPeer(peer2Pieces);

        for (var i = 0; i < 5; i++)
        {
            Assert.That(histogram.GetAvailability(i), Is.EqualTo(0), $"Piece {i} leaked ghost count!");
        }

        Assert.That(histogram.GetBucketPieceCount(0), Is.EqualTo(5));
        Assert.That(histogram.GetBucketPieceCount(1), Is.EqualTo(0));
        Assert.That(histogram.GetBucketPieceCount(2), Is.EqualTo(0));
    }

    [Test]
    public void GetRarestPieces_returns_lowest_availability_pieces_first()
    {
        var histogram = new SwarmPieceHistogram(6);

        // Piece 0: availability 3
        histogram.IncrementPiece(0);
        histogram.IncrementPiece(0);
        histogram.IncrementPiece(0);

        // Piece 1: availability 1 (rarest in swarm)
        histogram.IncrementPiece(1);

        // Piece 2: availability 2
        histogram.IncrementPiece(2);
        histogram.IncrementPiece(2);

        // Piece 3: availability 1 (rarest in swarm)
        histogram.IncrementPiece(3);

        // Piece 4: availability 2
        histogram.IncrementPiece(4);
        histogram.IncrementPiece(4);

        // Piece 5: availability 0 (not in swarm)

        var localMissing = new[] { true, true, true, true, true, true };
        var peerPieces = new[] { true, true, true, true, true, true };

        var rarest2 = histogram.GetRarestPieces(localMissing, peerPieces, 2);

        Assert.That(rarest2.Count, Is.EqualTo(2));
        Assert.That(rarest2, Is.EquivalentTo(new[] { 1, 3 }));

        var rarest4 = histogram.GetRarestPieces(localMissing, peerPieces, 4);

        Assert.That(rarest4.Count, Is.EqualTo(4));
        Assert.That(rarest4[0] == 1 || rarest4[0] == 3, Is.True);
        Assert.That(rarest4[1] == 1 || rarest4[1] == 3, Is.True);
        Assert.That(rarest4[2] == 2 || rarest4[2] == 4, Is.True);
        Assert.That(rarest4[3] == 2 || rarest4[3] == 4, Is.True);

        var rarestAll = histogram.GetRarestPieces(localMissing, peerPieces, 10);

        Assert.That(rarestAll.Count, Is.EqualTo(5)); // Piece 5 has 0 availability so not returned
        Assert.That(rarestAll[4], Is.EqualTo(0)); // Piece 0 has highest availability (3), so last
    }

    [Test]
    public void GetRarestPieces_filters_by_local_missing_and_peer_pieces()
    {
        var histogram = new SwarmPieceHistogram(4);
        histogram.IncrementPiece(0);
        histogram.IncrementPiece(1);
        histogram.IncrementPiece(2);
        histogram.IncrementPiece(3);

        // Local already has piece 0
        var localMissing = new[] { false, true, true, true };

        // Peer does not have piece 1
        var peerPieces = new[] { true, false, true, true };

        var results = histogram.GetRarestPieces(localMissing, peerPieces, 5);

        Assert.That(results, Is.EquivalentTo(new[] { 2, 3 }));
    }

    [Test]
    public void BitArray_overloads_operate_identically_to_bool_arrays()
    {
        var histogram = new SwarmPieceHistogram(4);
        var peerPieces = new BitArray(new[] { true, false, true, false });

        histogram.RegisterPeer(peerPieces);

        Assert.That(histogram.GetAvailability(0), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(1), Is.EqualTo(0));
        Assert.That(histogram.GetAvailability(2), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(3), Is.EqualTo(0));

        var localMissing = new BitArray(new[] { true, true, true, true });
        var rarest = histogram.GetRarestPieces(localMissing, peerPieces, 2);

        Assert.That(rarest, Is.EquivalentTo(new[] { 0, 2 }));

        histogram.UnregisterPeer(peerPieces);

        Assert.That(histogram.GetAvailability(0), Is.EqualTo(0));
        Assert.That(histogram.GetAvailability(2), Is.EqualTo(0));
    }

    [Test]
    public void Boundary_cases_handle_empty_histogram_and_out_of_bounds()
    {
        var empty = new SwarmPieceHistogram(0);

        Assert.That(empty.TotalPieces, Is.EqualTo(0));
        Assert.That(empty.GetAvailability(0), Is.EqualTo(0));
        Assert.That(empty.GetRarestPieces(new[] { true }, new[] { true }, 5), Is.Empty);

        var histogram = new SwarmPieceHistogram(3);

        // Out of bounds operations must not throw
        Assert.DoesNotThrow(() => histogram.IncrementPiece(-1));
        Assert.DoesNotThrow(() => histogram.IncrementPiece(10));
        Assert.DoesNotThrow(() => histogram.DecrementPiece(-1));
        Assert.DoesNotThrow(() => histogram.DecrementPiece(10));
        Assert.DoesNotThrow(() => histogram.RegisterPeer((bool[])null));
        Assert.DoesNotThrow(() => histogram.UnregisterPeer((bool[])null));
        Assert.DoesNotThrow(() => histogram.RegisterPeer((BitArray)null));
        Assert.DoesNotThrow(() => histogram.UnregisterPeer((BitArray)null));

        Assert.That(histogram.GetAvailability(-1), Is.EqualTo(0));
        Assert.That(histogram.GetAvailability(10), Is.EqualTo(0));
        Assert.That(histogram.GetBucketPieceCount(-1), Is.EqualTo(0));
        Assert.That(histogram.GetBucketPieceCount(10), Is.EqualTo(0));
        Assert.That(histogram.GetPiecesInBucket(-1), Is.Empty);
        Assert.That(histogram.GetPiecesInBucket(10), Is.Empty);
        Assert.That(histogram.GetRarestPieces((bool[])null, (bool[])null, 0), Is.Empty);
        Assert.That(histogram.GetRarestPieces((bool[])null, (bool[])null, -1), Is.Empty);
        Assert.That(histogram.GetRarestPieces((BitArray)null, (BitArray)null, 0), Is.Empty);
    }

    [Test]
    public void PeerConnection_HaveCount_is_updated_incrementally()
    {
        var connection = new PeerConnection(null);

        Assert.That(connection.HaveCount, Is.EqualTo(0));

        connection.PeerPieces = new[] { true, false, true, false, true };

        Assert.That(connection.HaveCount, Is.EqualTo(3));
        Assert.That(connection.IsSeed, Is.False);

        connection.HaveCount++;

        Assert.That(connection.HaveCount, Is.EqualTo(4));

        connection.PeerPieces = new[] { true, true, true, true, true };

        Assert.That(connection.HaveCount, Is.EqualTo(5));
        Assert.That(connection.IsSeed, Is.True);

        connection.PeerPieces = null;

        Assert.That(connection.HaveCount, Is.EqualTo(0));
        Assert.That(connection.IsSeed, Is.False);
    }
}
