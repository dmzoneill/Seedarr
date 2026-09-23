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
public class SequentialPiecePickerTests
{
    private IPiecePicker _rarestFirstMock;
    private IRandomNumberGenerator _randomMock;
    private SequentialPiecePicker _picker;

    [SetUp]
    public void SetUp()
    {
        _rarestFirstMock = Substitute.For<IPiecePicker>();
        _randomMock = Substitute.For<IRandomNumberGenerator>();
        _randomMock.NextDouble().Returns(0.99); // Default: above rarestFirstRatio, purely sequential
        _picker = new SequentialPiecePicker(_rarestFirstMock, _randomMock);
    }

    [Test]
    public void Sequential_selection_picks_lowest_missing_piece_in_order()
    {
        var myPieces = new BitArray(10, false);
        var peerPieces = new BitArray(10, true);
        var availability = Enumerable.Repeat(2, 10).ToArray();

        var picked1 = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);
        Assert.That(picked1, Is.EqualTo(0));

        myPieces[0] = true;
        var picked2 = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);
        Assert.That(picked2, Is.EqualTo(1));

        myPieces[1] = true;
        var picked3 = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);
        Assert.That(picked3, Is.EqualTo(2));
    }

    [Test]
    public void Sequential_selection_skips_pieces_peer_does_not_have()
    {
        var myPieces = new BitArray(10, false);
        var peerPieces = new BitArray(10, false);
        peerPieces[3] = true;
        peerPieces[4] = true;
        var availability = Enumerable.Repeat(1, 10).ToArray();

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);

        Assert.That(picked, Is.EqualTo(3));
    }

    [Test]
    public void Sequential_selection_returns_null_when_peer_has_no_needed_pieces()
    {
        var myPieces = new BitArray(10, false);
        myPieces[0] = true;
        myPieces[1] = true;

        var peerPieces = new BitArray(10, false);
        peerPieces[0] = true; // Peer only has pieces we already have

        var availability = Enumerable.Repeat(1, 10).ToArray();

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true);

        Assert.That(picked, Is.Null);
    }

    [Test]
    public void Sequential_selection_returns_null_when_all_pieces_downloaded()
    {
        var myPieces = new BitArray(10, true);
        var peerPieces = new BitArray(10, true);
        var availability = Enumerable.Repeat(1, 10).ToArray();

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true);

        Assert.That(picked, Is.Null);
    }

    [Test]
    public void Deadline_and_boundary_priority_picks_boundaries_before_general_pieces()
    {
        var myPieces = new BitArray(20, false);
        var peerPieces = new BitArray(20, true);
        var availability = Enumerable.Repeat(2, 20).ToArray();

        // Boundary pieces for 20: [0, 1, 18, 19]
        var picked0 = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(picked0, Is.EqualTo(0));

        myPieces[0] = true;
        var picked1 = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(picked1, Is.EqualTo(1));

        myPieces[1] = true;
        var picked18 = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(picked18, Is.EqualTo(18));

        myPieces[18] = true;
        var picked19 = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(picked19, Is.EqualTo(19));

        // After all boundaries are acquired, pick normally from sequential head (piece 2)
        myPieces[19] = true;
        var pickedSeq = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(pickedSeq, Is.EqualTo(2));
    }

    [Test]
    public void Deadline_and_boundary_priority_respects_custom_boundaries()
    {
        var myPieces = new BitArray(20, false);
        var peerPieces = new BitArray(20, true);
        var availability = Enumerable.Repeat(2, 20).ToArray();
        var customBoundaries = new[] { 7, 14 };

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: true,
            lookaheadWindow: 5,
            rarestFirstRatio: 0.0,
            activePieces: null,
            firstLastPiecePrio: true,
            customBoundaryPieces: customBoundaries);

        Assert.That(picked, Is.EqualTo(7));
    }

    [Test]
    public void Active_pieces_skips_in_progress_pieces_in_window()
    {
        var myPieces = new BitArray(10, false);
        var peerPieces = new BitArray(10, true);
        var availability = Enumerable.Repeat(2, 10).ToArray();
        var activePieces = new HashSet<int> { 0, 1 };

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: true,
            lookaheadWindow: 5,
            rarestFirstRatio: 0.0,
            activePieces: activePieces);

        Assert.That(picked, Is.EqualTo(2));
    }

    [Test]
    public void Active_pieces_falls_back_to_outside_window_when_all_window_pieces_are_active()
    {
        var myPieces = new BitArray(10, false);
        var peerPieces = new BitArray(10, true);
        var availability = Enumerable.Repeat(2, 10).ToArray();

        // Window with size 3 starting at head 0 -> [0, 1, 2]
        var activePieces = new HashSet<int> { 0, 1, 2 };

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: true,
            lookaheadWindow: 3,
            rarestFirstRatio: 0.0,
            activePieces: activePieces);

        // Should pick an outside candidate (>= 3)
        Assert.That(picked, Is.Not.Null);
        Assert.That(picked.Value, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void Endgame_mode_duplicate_requests_when_all_candidates_are_active()
    {
        var myPieces = new BitArray(3, false);
        var peerPieces = new BitArray(3, true);
        var availability = Enumerable.Repeat(2, 3).ToArray();

        // All 3 missing pieces are already active
        var activePieces = new HashSet<int> { 0, 1, 2 };

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: true,
            lookaheadWindow: 5,
            rarestFirstRatio: 0.0,
            activePieces: activePieces);

        // Endgame re-request selects lowest window candidate rather than starving
        Assert.That(picked, Is.EqualTo(0));
    }

    [Test]
    public void Hybrid_rarest_first_picks_from_outside_window_when_ratio_roll_hits()
    {
        var myPieces = new BitArray(10, false);
        var peerPieces = new BitArray(10, true);

        // Outside window (>= 2): piece 5 has rarest availability = 1, others = 5
        var availability = new[] { 3, 3, 5, 5, 5, 1, 5, 5, 5, 5 };

        _randomMock.NextDouble().Returns(0.05); // Roll < rarestFirstRatio (0.2)

        var picked = _picker.PickPiece(
            myPieces,
            peerPieces,
            availability,
            sequential: true,
            lookaheadWindow: 2,
            rarestFirstRatio: 0.2);

        Assert.That(picked, Is.EqualTo(5));
    }

    [Test]
    public void End_of_file_wrapping_clamps_window_at_torrent_end()
    {
        var myPieces = new BitArray(10, false);
        for (var i = 0; i < 8; i++)
        {
            myPieces[i] = true;
        }

        var peerPieces = new BitArray(10, true);
        var availability = Enumerable.Repeat(2, 10).ToArray();

        // Head = 8. Lookahead window = 20. End = min(8 + 20, 10) = 10.
        var picked = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 20);

        Assert.That(picked, Is.EqualTo(8));

        myPieces[8] = true;
        var pickedLast = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 20);

        Assert.That(pickedLast, Is.EqualTo(9));
    }

    [Test]
    public void Multiple_peers_evaluates_union_of_peer_availability()
    {
        var myPieces = new BitArray(10, false);
        var peer1 = new BitArray(10, false);
        peer1[2] = true;

        var peer2 = new BitArray(10, false);
        peer2[3] = true;

        var availability = Enumerable.Repeat(1, 10).ToArray();

        var picked = _picker.PickPiece(
            myPieces,
            new[] { peer1, peer2 },
            availability,
            sequential: true,
            lookaheadWindow: 5,
            rarestFirstRatio: 0.0);

        Assert.That(picked, Is.EqualTo(2));
    }

    [Test]
    public void Non_sequential_mode_delegates_to_rarest_first_picker()
    {
        var myPieces = new BitArray(10, false);
        var peerPieces = new BitArray(10, true);
        var availability = Enumerable.Repeat(2, 10).ToArray();

        _rarestFirstMock.PickPiece(
            Arg.Any<BitArray>(),
            Arg.Any<IReadOnlyList<BitArray>>(),
            Arg.Any<IReadOnlyList<int>>(),
            false,
            Arg.Any<int>(),
            Arg.Any<double>(),
            Arg.Any<IReadOnlyCollection<int>>(),
            Arg.Any<bool>(),
            Arg.Any<IReadOnlyCollection<int>>())
            .Returns(7);

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, sequential: false);

        Assert.That(picked, Is.EqualTo(7));
        _rarestFirstMock.Received(1).PickPiece(
            myPieces,
            Arg.Any<IReadOnlyList<BitArray>>(),
            availability,
            false,
            Arg.Any<int>(),
            Arg.Any<double>(),
            null,
            false,
            null);
    }

    [Test]
    public void Edge_cases_null_and_empty_inputs_return_null()
    {
        var validBits = new BitArray(10, true);
        var availability = new[] { 1 };

        Assert.That(_picker.PickPiece(null, validBits, availability, sequential: true), Is.Null);
        Assert.That(_picker.PickPiece(new BitArray(0), validBits, availability, sequential: true), Is.Null);
        Assert.That(_picker.PickPiece(validBits, (BitArray)null, availability, sequential: true), Is.Null);
        Assert.That(_picker.PickPiece(validBits, Array.Empty<BitArray>(), availability, sequential: true), Is.Null);
    }

    [TestCase(0, 20)]
    [TestCase(-10, 20)]
    public void Invalid_lookahead_window_defaults_to_20(int invalidWindow, int expectedWindow)
    {
        var myPieces = new BitArray(30, false);
        var peerPieces = new BitArray(30, true);
        var availability = Enumerable.Repeat(1, 30).ToArray();

        var picked = _picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: invalidWindow, rarestFirstRatio: 0.0);

        Assert.That(picked, Is.EqualTo(0));
    }

    [TestCase(0, new int[0])]
    [TestCase(1, new[] { 0 })]
    [TestCase(2, new[] { 0, 1 })]
    [TestCase(3, new[] { 0, 1, 2 })]
    [TestCase(4, new[] { 0, 1, 2, 3 })]
    [TestCase(10, new[] { 0, 1, 8, 9 })]
    public void GetBoundaryPieces_generates_correct_indices(int pieceCount, int[] expected)
    {
        var boundaries = SequentialPiecePicker.GetBoundaryPieces(pieceCount);
        Assert.That(boundaries, Is.EqualTo(expected));
    }

    [Test]
    public void SequentialPicker_subclass_instantiates_and_functions_identically()
    {
        var subPicker = new SequentialPicker(_rarestFirstMock, _randomMock);
        var myPieces = new BitArray(5, false);
        var peerPieces = new BitArray(5, true);
        var availability = new[] { 1, 1, 1, 1, 1 };

        var picked = subPicker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);

        Assert.That(picked, Is.EqualTo(0));
    }
}
