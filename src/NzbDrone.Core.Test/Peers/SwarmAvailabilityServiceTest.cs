using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Torrents;
using PeersPicker = NzbDrone.Core.Peers.PiecePicker;
using PiecesPicker = NzbDrone.Core.Pieces;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class SwarmAvailabilityServiceTest
{
    [Test]
    public void CalculateAvailability_returns_zero_when_piece_count_is_zero_or_negative()
    {
        var torrent = new Torrent
        {
            PieceCount = 0,
            Progress = 0.5
        };

        var avail = SwarmAvailabilityService.CalculateAvailability(0, null, new List<PeerConnection>());
        Assert.That(avail, Is.EqualTo(0.0));

        var (torrentAvail, _, _) = SwarmAvailabilityService.CalculateSwarmAvailability(torrent, new List<PeerConnection>());
        Assert.That(torrentAvail, Is.EqualTo(0.0));
    }

    [Test]
    public void CalculateAvailability_matches_canonical_formula_for_zero_copies()
    {
        // N = 4 pieces. Local has no pieces. No peers.
        var localPieces = new bool[4];
        var peers = new List<PeerConnection>();

        var avail = SwarmAvailabilityService.CalculateAvailability(4, localPieces, peers);
        Assert.That(avail, Is.EqualTo(0.0));
    }

    [Test]
    public void CalculateAvailability_matches_canonical_formula_for_one_full_copy_locally()
    {
        // N = 4 pieces. Local has all pieces. No peers.
        // A_i = 1 for all i. min(A_i) = 1, count(A_i > 1) = 0.
        // Availability = 1 + 0/4 = 1.0.
        var localPieces = new[] { true, true, true, true };
        var peers = new List<PeerConnection>();

        var avail = SwarmAvailabilityService.CalculateAvailability(4, localPieces, peers);
        Assert.That(avail, Is.EqualTo(1.0));
    }

    [Test]
    public void CalculateAvailability_matches_canonical_formula_for_one_full_copy_from_single_seed()
    {
        // N = 4 pieces. Local has 0 pieces. 1 seed peer.
        // A_i = 1 for all i. min(A_i) = 1, count(A_i > 1) = 0.
        // Availability = 1 + 0/4 = 1.0.
        using var ms = new MemoryStream();
        var seed = new PeerConnection(ms, "192.168.1.1", 6881)
        {
            Progress = 1.0
        };

        var avail = SwarmAvailabilityService.CalculateAvailability(4, new bool[4], new[] { seed });
        Assert.That(avail, Is.EqualTo(1.0));
    }

    [Test]
    public void CalculateAvailability_matches_canonical_formula_for_multiple_seeds()
    {
        // N = 4 pieces. Local has all pieces (1 copy). 2 connected seed peers (2 copies).
        // Total copies per piece = 3. min(A_i) = 3, count(A_i > 3) = 0.
        // Availability = 3.0.
        using var ms1 = new MemoryStream();
        using var ms2 = new MemoryStream();
        var seed1 = new PeerConnection(ms1, "192.168.1.1", 6881) { Progress = 1.0 };
        var seed2 = new PeerConnection(ms2, "192.168.1.2", 6881) { Progress = 1.0 };

        var localPieces = new[] { true, true, true, true };
        var avail = SwarmAvailabilityService.CalculateAvailability(4, localPieces, new[] { seed1, seed2 });
        Assert.That(avail, Is.EqualTo(3.0));
    }

    [Test]
    public void CalculateAvailability_matches_canonical_formula_for_distributed_swarm()
    {
        // N = 4 pieces.
        // Local has piece 0, 1: [true, true, false, false]
        // Peer 1 has piece 2, 3: [false, false, true, true]
        // Composite: each piece has exactly 1 copy.
        // min(A_i) = 1, count(A_i > 1) = 0 -> Availability = 1.0.
        using var ms1 = new MemoryStream();
        var peer1 = new PeerConnection(ms1, "192.168.1.1", 6881)
        {
            PeerPieces = new[] { false, false, true, true }
        };

        var localPieces = new[] { true, true, false, false };
        var avail = SwarmAvailabilityService.CalculateAvailability(4, localPieces, new[] { peer1 });
        Assert.That(avail, Is.EqualTo(1.0));
    }

    [Test]
    public void CalculateAvailability_matches_canonical_formula_for_partial_and_fractional_availability()
    {
        // N = 4 pieces.
        // Local: [1, 1, 0, 0]
        // Peer 1: [1, 0, 0, 0]
        // A = [2, 1, 0, 0]. min(A_i) = 0.
        // Pieces where A_i > 0: piece 0 (2) and piece 1 (1) -> count = 2.
        // Availability = 0 + 2/4 = 0.5.
        using var ms1 = new MemoryStream();
        var peer1 = new PeerConnection(ms1, "192.168.1.1", 6881)
        {
            PeerPieces = new[] { true, false, false, false }
        };

        var localPieces = new[] { true, true, false, false };
        var avail = SwarmAvailabilityService.CalculateAvailability(4, localPieces, new[] { peer1 });
        Assert.That(avail, Is.EqualTo(0.5));
    }

    [Test]
    public void CalculateAvailability_matches_canonical_formula_for_heterogeneous_multi_peer_swarm()
    {
        // N = 4 pieces.
        // Local: [1, 1, 1, 1] (all 4 pieces)
        // Peer 1: [1, 1, 1, 0] (3 pieces)
        // A = [2, 2, 2, 1]. min(A_i) = 1.
        // Pieces with A_i > 1: pieces 0, 1, 2 -> count = 3.
        // Availability = 1 + 3/4 = 1.75!
        using var ms1 = new MemoryStream();
        var peer1 = new PeerConnection(ms1, "192.168.1.1", 6881)
        {
            PeerPieces = new[] { true, true, true, false }
        };

        var localPieces = new[] { true, true, true, true };
        var avail = SwarmAvailabilityService.CalculateAvailability(4, localPieces, new[] { peer1 });
        Assert.That(avail, Is.EqualTo(1.75));
    }

    [Test]
    public void CalculateAvailability_from_histogram_frequencies_matches_canonical_formula()
    {
        // N = 5.
        // Local: [1, 0, 1, 0, 0]
        // Peer frequencies: [1, 2, 1, 2, 2]
        // A_i = local + peer:
        // A_0 = 1 + 1 = 2
        // A_1 = 0 + 2 = 2
        // A_2 = 1 + 1 = 2
        // A_3 = 0 + 2 = 2
        // A_4 = 0 + 2 = 2
        // All A_i = 2 -> min = 2, count > 2 = 0 -> Availability = 2.0.
        var localPieces = new[] { true, false, true, false, false };
        var frequencies = new[] { 1, 2, 1, 2, 2 };

        var avail = SwarmAvailabilityService.CalculateAvailability(5, localPieces, frequencies);
        Assert.That(avail, Is.EqualTo(2.0));
    }

    [Test]
    public void Disconnecting_peers_decrements_availability_histogram_properly()
    {
        var histogram = new SwarmPieceHistogram(4);

        // Peer 1 connects with pieces 0, 1
        var peer1Pieces = new[] { true, true, false, false };
        histogram.RegisterPeer(peer1Pieces);

        Assert.That(histogram.GetAvailability(0), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(1), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(2), Is.EqualTo(0));
        Assert.That(histogram.GetAvailability(3), Is.EqualTo(0));

        // Peer 2 connects with pieces 1, 2
        var peer2Pieces = new[] { false, true, true, false };
        histogram.RegisterPeer(peer2Pieces);

        Assert.That(histogram.GetAvailability(0), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(1), Is.EqualTo(2));
        Assert.That(histogram.GetAvailability(2), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(3), Is.EqualTo(0));

        // Peer 1 disconnects -> unregister
        histogram.UnregisterPeer(peer1Pieces);

        Assert.That(histogram.GetAvailability(0), Is.EqualTo(0));
        Assert.That(histogram.GetAvailability(1), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(2), Is.EqualTo(1));
        Assert.That(histogram.GetAvailability(3), Is.EqualTo(0));

        // Peer 2 disconnects -> unregister
        histogram.UnregisterPeer(peer2Pieces);

        // All pieces should be strictly 0 with no leaked ghost counts
        for (var i = 0; i < 4; i++)
        {
            Assert.That(histogram.GetAvailability(i), Is.EqualTo(0), $"Piece {i} leaked count after peer disconnect!");
        }
    }

    [Test]
    public void Rarest_first_picker_selects_lowest_frequency_pieces()
    {
        // Test both RarestFirstPiecePicker and RarestFirstPicker in Peers and Pieces namespaces
        var pickers = new PeersPicker.IPiecePicker[]
        {
            new PeersPicker.RarestFirstPiecePicker(),
            new PeersPicker.RarestFirstPicker(),
            new PiecesPicker.RarestFirstPiecePicker(),
            new PiecesPicker.RarestFirstPicker()
        };

        foreach (var picker in pickers)
        {
            // 5 pieces. We have piece 0. Missing: 1, 2, 3, 4.
            var myPieces = new BitArray(5, false);
            myPieces[0] = true;

            // Peer has pieces 1, 2, 3, 4.
            var peerPieces = new BitArray(5, true);

            // Swarm frequencies: piece 3 has frequency 1 (rarest non-zero frequency)
            var availability = new[] { 10, 4, 3, 1, 5 };

            var picked = picker.PickPiece(myPieces, peerPieces, availability, sequential: false);
            Assert.That(picked, Is.EqualTo(3), $"Picker {picker.GetType().FullName} failed to select rarest piece");

            // When piece 3 is completed, next rarest is piece 2 (freq = 3)
            myPieces[3] = true;
            var nextPicked = picker.PickPiece(myPieces, peerPieces, availability, sequential: false);
            Assert.That(nextPicked, Is.EqualTo(2), $"Picker {picker.GetType().FullName} failed to select next rarest piece");
        }
    }

    [Test]
    public void Sequential_picker_selects_ascending_piece_order()
    {
        // Test both SequentialPiecePicker and SequentialPicker in Peers and Pieces namespaces
        var pickers = new PeersPicker.IPiecePicker[]
        {
            new PeersPicker.SequentialPiecePicker(),
            new PeersPicker.SequentialPicker(),
            new PiecesPicker.SequentialPiecePicker(),
            new PiecesPicker.SequentialPicker()
        };

        foreach (var picker in pickers)
        {
            // 6 pieces total. We have none.
            var myPieces = new BitArray(6, false);
            var peerPieces = new BitArray(6, true);
            var availability = new[] { 5, 4, 3, 2, 1, 6 }; // piece 4 is rarest, but sequential should ignore and pick ascending

            // 1. Picks piece 0
            var picked0 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 10, rarestFirstRatio: 0.0);
            Assert.That(picked0, Is.EqualTo(0), $"Picker {picker.GetType().FullName} did not pick ascending index 0");

            // 2. We complete piece 0 -> next is piece 1
            myPieces[0] = true;
            var picked1 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 10, rarestFirstRatio: 0.0);
            Assert.That(picked1, Is.EqualTo(1), $"Picker {picker.GetType().FullName} did not pick ascending index 1");

            // 3. We complete piece 1 -> next is piece 2
            myPieces[1] = true;
            var picked2 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 10, rarestFirstRatio: 0.0);
            Assert.That(picked2, Is.EqualTo(2), $"Picker {picker.GetType().FullName} did not pick ascending index 2");
        }
    }

    [Test]
    public void Have_message_increments_haveCount_in_O1()
    {
        using var ms = new MemoryStream();
        var connection = new PeerConnection(ms, "127.0.0.1", 6881);

        Assert.That(connection.HaveCount, Is.EqualTo(0));
        Assert.That(connection.Progress, Is.EqualTo(0.0));

        // Initial have message for piece 2 in a 10-piece torrent
        var newlyAdded1 = connection.RecordHave(2, 10);
        Assert.That(newlyAdded1, Is.True);
        Assert.That(connection.HaveCount, Is.EqualTo(1));
        Assert.That(connection.Progress, Is.EqualTo(0.1));
        Assert.That(connection.PeerPieces[2], Is.True);

        // Next have message for piece 5
        var newlyAdded2 = connection.RecordHave(5, 10);
        Assert.That(newlyAdded2, Is.True);
        Assert.That(connection.HaveCount, Is.EqualTo(2));
        Assert.That(connection.Progress, Is.EqualTo(0.2));
        Assert.That(connection.PeerPieces[5], Is.True);

        // Duplicate have message for piece 2 should not increment HaveCount
        var newlyAddedDup = connection.RecordHave(2, 10);
        Assert.That(newlyAddedDup, Is.False);
        Assert.That(connection.HaveCount, Is.EqualTo(2));
        Assert.That(connection.Progress, Is.EqualTo(0.2));
    }
}
