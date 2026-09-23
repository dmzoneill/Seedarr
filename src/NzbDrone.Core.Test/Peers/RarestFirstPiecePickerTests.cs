using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Peers.PiecePicker;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class RarestFirstPiecePickerTests
{
    private IRandomNumberGenerator _random;
    private RarestFirstPiecePicker _picker;

    [SetUp]
    public void SetUp()
    {
        _random = Substitute.For<IRandomNumberGenerator>();
        _picker = new RarestFirstPiecePicker(_random);
    }

    [Test]
    public void PickPiece_returns_null_when_myPieces_is_null_or_empty()
    {
        var peerPieces = new BitArray(5, true);
        var availability = new[] { 1, 1, 1, 1, 1 };

        Assert.That(_picker.PickPiece(null, peerPieces, availability, false), Is.Null);
        Assert.That(_picker.PickPiece(new BitArray(0), peerPieces, availability, false), Is.Null);
    }

    [Test]
    public void PickPiece_returns_null_when_peerPieces_is_null_or_empty()
    {
        var myPieces = new BitArray(5, false);
        var availability = new[] { 1, 1, 1, 1, 1 };

        Assert.That(_picker.PickPiece(myPieces, (BitArray)null, availability, false), Is.Null);
        Assert.That(_picker.PickPiece(myPieces, (IReadOnlyList<BitArray>)null, availability, false), Is.Null);
        Assert.That(_picker.PickPiece(myPieces, Array.Empty<BitArray>(), availability, false), Is.Null);
    }

    [Test]
    public void PickPiece_returns_null_when_all_pieces_already_downloaded()
    {
        var myPieces = new BitArray(4, true);
        var peerPieces = new BitArray(4, true);
        var availability = new[] { 2, 2, 2, 2 };

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, false);

        Assert.That(picked, Is.Null);
    }

    [Test]
    public void PickPiece_returns_null_when_peer_has_no_needed_pieces()
    {
        var myPieces = new BitArray(4, false);
        myPieces[0] = true;
        myPieces[1] = true;

        var peerPieces = new BitArray(4, false);
        peerPieces[0] = true; // Peer only has pieces we already possess

        var availability = new[] { 1, 1, 1, 1 };

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, false);

        Assert.That(picked, Is.Null);
    }

    [Test]
    public void PickPiece_selects_piece_with_lowest_availability_in_swarm()
    {
        var myPieces = new BitArray(5, false);
        var peerPieces = new BitArray(5, true);
        var availability = new[] { 10, 8, 2, 9, 5 };

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, false);

        Assert.That(picked, Is.EqualTo(2)); // Availability 2 is the lowest
    }

    [Test]
    public void PickPiece_ignores_already_possessed_pieces_even_if_rarest()
    {
        var myPieces = new BitArray(5, false);
        myPieces[2] = true; // We already have piece 2

        var peerPieces = new BitArray(5, true);
        var availability = new[] { 10, 8, 1, 9, 3 };

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, false);

        Assert.That(picked, Is.EqualTo(4)); // Availability 3 is rarest among unpossessed
    }

    [Test]
    public void PickPiece_derives_availability_from_peer_bitarrays_when_availability_array_is_null()
    {
        var myPieces = new BitArray(3, false);

        // 3 peers in the swarm:
        // Peer 0 has {0, 1, 2}
        // Peer 1 has {1, 2}
        // Peer 2 has {2}
        // Swarm counts: piece 0 = 1, piece 1 = 2, piece 2 = 3
        var peer0 = new BitArray(3, false);
        peer0[0] = true;
        peer0[1] = true;
        peer0[2] = true;

        var peer1 = new BitArray(3, false);
        peer1[1] = true;
        peer1[2] = true;

        var peer2 = new BitArray(3, false);
        peer2[2] = true;

        var peers = new[] { peer0, peer1, peer2 };

        var picked = _picker.PickPiece(myPieces, peers, null, false);

        Assert.That(picked, Is.EqualTo(0)); // Piece 0 has frequency 1
    }

    [Test]
    public void PickPiece_breaks_ties_using_random_number_generator()
    {
        var myPieces = new BitArray(4, false);
        var peerPieces = new BitArray(4, true);

        // Pieces 1 and 3 are tied for lowest availability (count = 2)
        var availability = new[] { 5, 2, 7, 2 };

        // Test branch when random picks index 0 (tied piece 1)
        _random.Next(2).Returns(0);
        var pickedFirst = _picker.PickPiece(myPieces, peerPieces, availability, false);
        Assert.That(pickedFirst, Is.EqualTo(1));

        // Test branch when random picks index 1 (tied piece 3)
        _random.Next(2).Returns(1);
        var pickedSecond = _picker.PickPiece(myPieces, peerPieces, availability, false);
        Assert.That(pickedSecond, Is.EqualTo(3));
    }

    [Test]
    public void PickPiece_does_not_invoke_random_when_no_tie_exists()
    {
        var myPieces = new BitArray(3, false);
        var peerPieces = new BitArray(3, true);
        var availability = new[] { 10, 1, 5 };

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, false);

        Assert.That(picked, Is.EqualTo(1));
        _random.DidNotReceive().Next(Arg.Any<int>());
    }

    [Test]
    public void PickPiece_prioritizes_boundary_pieces_when_firstLastPiecePrio_is_true()
    {
        // 20 pieces: SequentialPiecePicker boundary pieces are [0, 1, 18, 19]
        var myPieces = new BitArray(20, false);
        var peerPieces = new BitArray(20, true);

        var availability = Enumerable.Repeat(10, 20).ToArray();
        availability[10] = 1; // Piece 10 is rarest, but not a boundary piece

        var picked0 = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: false,
            lookaheadWindow: 20,
            rarestFirstRatio: 0.2,
            activePieces: null,
            firstLastPiecePrio: true);

        Assert.That(picked0, Is.EqualTo(0));

        myPieces[0] = true;
        var picked1 = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: false,
            lookaheadWindow: 20,
            rarestFirstRatio: 0.2,
            activePieces: null,
            firstLastPiecePrio: true);

        Assert.That(picked1, Is.EqualTo(1));
    }

    [Test]
    public void PickPiece_respects_customBoundaryPieces_and_skips_out_of_range_or_possessed_boundaries()
    {
        var myPieces = new BitArray(10, false);
        myPieces[3] = true; // Already possess boundary piece 3

        var peerPieces = new BitArray(10, true);
        var availability = new[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 };

        var customBoundaries = new[] { -1, 3, 7, 99 };

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: false,
            lookaheadWindow: 20,
            rarestFirstRatio: 0.2,
            activePieces: null,
            firstLastPiecePrio: true,
            customBoundaryPieces: customBoundaries);

        Assert.That(picked, Is.EqualTo(7));
    }

    [Test]
    public void PickPiece_skips_boundary_pieces_that_are_currently_active()
    {
        var myPieces = new BitArray(10, false);
        var peerPieces = new BitArray(10, true);
        var availability = Enumerable.Repeat(5, 10).ToArray();
        var customBoundaries = new[] { 2, 4 };
        var activePieces = new HashSet<int> { 2 };

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: false,
            lookaheadWindow: 20,
            rarestFirstRatio: 0.2,
            activePieces: activePieces,
            firstLastPiecePrio: true,
            customBoundaryPieces: customBoundaries);

        Assert.That(picked, Is.EqualTo(4));
    }

    [Test]
    public void PickPiece_falls_back_to_rarest_first_when_all_boundary_pieces_are_possessed()
    {
        var myPieces = new BitArray(6, false);
        myPieces[1] = true;
        myPieces[2] = true;

        var peerPieces = new BitArray(6, true);
        var availability = new[] { 8, 8, 8, 9, 2, 7 };
        var customBoundaries = new[] { 1, 2 };

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: false,
            lookaheadWindow: 20,
            rarestFirstRatio: 0.2,
            activePieces: null,
            firstLastPiecePrio: true,
            customBoundaryPieces: customBoundaries);

        Assert.That(picked, Is.EqualTo(4)); // Availability 2
    }

    [Test]
    public void PickPiece_filters_out_active_pieces_when_non_active_candidates_exist()
    {
        var myPieces = new BitArray(5, false);
        var peerPieces = new BitArray(5, true);

        // Piece 1 is rarest (avail 1), but already requested (active)
        // Piece 3 is next rarest (avail 2), and inactive
        var availability = new[] { 10, 1, 9, 2, 8 };
        var activePieces = new HashSet<int> { 1 };

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: false,
            lookaheadWindow: 20,
            rarestFirstRatio: 0.2,
            activePieces: activePieces);

        Assert.That(picked, Is.EqualTo(3));
    }

    [Test]
    public void PickPiece_enters_endgame_mode_and_duplicates_requests_when_all_missing_pieces_are_active()
    {
        // 5 pieces total: we already have 0, 1, 2. Missing: 3, 4.
        var myPieces = new BitArray(5, false);
        myPieces[0] = true;
        myPieces[1] = true;
        myPieces[2] = true;

        var peerPieces = new BitArray(5, true);

        // In endgame mode: all remaining missing pieces (3 and 4) are already in activePieces
        var activePieces = new HashSet<int> { 3, 4 };
        var availability = new[] { 10, 10, 10, 4, 1 }; // Piece 4 is rarer than 3

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: false,
            lookaheadWindow: 20,
            rarestFirstRatio: 0.2,
            activePieces: activePieces);

        // Instead of returning null, endgame mode allows duplicate request of rarest in-flight piece
        Assert.That(picked, Is.EqualTo(4));
    }

    [Test]
    public void PickPiece_considers_union_of_pieces_across_multiple_peers()
    {
        var myPieces = new BitArray(4, false);

        var peer1 = new BitArray(4, false);
        peer1[0] = true; // Peer 1 has only piece 0 (availability 5)

        var peer2 = new BitArray(4, false);
        peer2[2] = true; // Peer 2 has only piece 2 (availability 1)

        var availability = new[] { 5, 10, 1, 10 };
        var peers = new[] { peer1, peer2 };

        var picked = _picker.PickPiece(myPieces, peers, availability, false);

        Assert.That(picked, Is.EqualTo(2));
    }

    [Test]
    public void PickPiece_overloads_delegate_properly()
    {
        var myPieces = new BitArray(3, false);
        var peerPieces = new BitArray(3, true);
        var availability = new[] { 5, 2, 8 };

        // Overload 1: single BitArray, 6 args
        var res1 = _picker.PickPiece(myPieces, peerPieces, availability, false, 10, 0.5);
        Assert.That(res1, Is.EqualTo(1));

        // Overload 2: peer list, 6 args
        var res2 = _picker.PickPiece(myPieces, new[] { peerPieces }, availability, false, 10, 0.5);
        Assert.That(res2, Is.EqualTo(1));

        // Overload 3: single BitArray, 7 args with activePieces
        var res3 = _picker.PickPiece(myPieces, peerPieces, availability, false, 10, 0.5, new[] { 1 });
        Assert.That(res3, Is.EqualTo(0));

        // Overload 4: peer list, 7 args with activePieces
        var res4 = _picker.PickPiece(myPieces, new[] { peerPieces }, availability, false, 10, 0.5, new[] { 1 });
        Assert.That(res4, Is.EqualTo(0));
    }

    [Test]
    public void PickRarest_returns_negative_one_when_candidates_list_is_empty()
    {
        var result = _picker.PickRarest(new List<int>(), new[] { 1, 2, 3 }, null);

        Assert.That(result, Is.EqualTo(-1));
    }

    [Test]
    public void GetAvailability_handles_array_peer_fallback_and_defaults()
    {
        var peer = new BitArray(3, false);
        peer[1] = true;
        var peers = new[] { peer };

        // Availability array has valid count
        var avail1 = RarestFirstPiecePicker.GetAvailability(0, new[] { 5 }, peers);
        Assert.That(avail1, Is.EqualTo(5));

        // Availability array has 0 -> falls back to peer count
        var avail2 = RarestFirstPiecePicker.GetAvailability(1, new[] { 0, 0 }, peers);
        Assert.That(avail2, Is.EqualTo(1));

        // Piece index beyond availability length -> falls back to peer count
        var avail3 = RarestFirstPiecePicker.GetAvailability(1, new[] { 9 }, peers);
        Assert.That(avail3, Is.EqualTo(1));

        // Neither availability array nor peers have piece -> defaults to 1
        var avail4 = RarestFirstPiecePicker.GetAvailability(2, null, peers);
        Assert.That(avail4, Is.EqualTo(1));

        // Both null -> defaults to 1
        var avail5 = RarestFirstPiecePicker.GetAvailability(0, null, null);
        Assert.That(avail5, Is.EqualTo(1));
    }

    [Test]
    public void RarestFirstPicker_subclass_functions_identically_to_base_class()
    {
        var picker = new RarestFirstPicker(_random);
        var myPieces = new BitArray(3, false);
        var peerPieces = new BitArray(3, true);
        var availability = new[] { 4, 1, 6 };

        var picked = picker.PickPiece(myPieces, peerPieces, availability, false);

        Assert.That(picked, Is.EqualTo(1));
    }
}
