using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.PiecePicker;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class PiecePickerTests
{
    private class FakeRandom : IRandomNumberGenerator
    {
        public double DoubleValue { get; set; }
        public int IntValue { get; set; }

        public int Next() => IntValue;
        public int Next(int maxValue) => IntValue % maxValue;
        public int Next(int minValue, int maxValue) => minValue + (IntValue % (maxValue - minValue));
        public double NextDouble() => DoubleValue;
    }

    [Test]
    public void Sequential_picker_returns_contiguous_missing_pieces_in_order()
    {
        var picker = new SequentialPiecePicker();

        // 10 pieces total. We have piece 0 and 1. Missing: 2..9
        var myPieces = new BitArray(10, false);
        myPieces[0] = true;
        myPieces[1] = true;

        var peerPieces = new BitArray(10, true);
        var availability = new[] { 2, 2, 5, 4, 3, 2, 1, 2, 3, 4 };

        // Sequential picker with rarestFirstRatio = 0.0 (strictly sequential)
        var picked1 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);
        Assert.That(picked1, Is.EqualTo(2));

        myPieces[2] = true;
        var picked2 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);
        Assert.That(picked2, Is.EqualTo(3));

        myPieces[3] = true;
        var picked3 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);
        Assert.That(picked3, Is.EqualTo(4));
    }

    [Test]
    public void Sequential_picker_respects_lookahead_window_bounds()
    {
        var picker = new SequentialPiecePicker();

        // 20 pieces. We have 0..4. Missing: 5..19. Lookahead window = 5 -> window is [5, 10)
        var myPieces = new BitArray(20, false);
        for (var i = 0; i < 5; i++)
        {
            myPieces[i] = true;
        }

        // Peer has pieces 7 (inside window [5, 10)) and 15 (outside window [10, 20))
        var peerPieces = new BitArray(20, false);
        peerPieces[7] = true;
        peerPieces[15] = true;

        var availability = new int[20];
        Array.Fill(availability, 3);
        availability[15] = 1; // 15 is rarer, but outside window

        var picked = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0);

        // Under sequential download with 0 rarest-first ratio, must choose piece 7 inside window [5, 10)
        Assert.That(picked, Is.EqualTo(7));
    }

    [Test]
    public void Rarest_first_selection_chooses_lowest_availability_piece()
    {
        var picker = new RarestFirstPiecePicker();

        // 6 pieces. We have 0, 1. Missing: 2, 3, 4, 5
        var myPieces = new BitArray(6, false);
        myPieces[0] = true;
        myPieces[1] = true;

        // Peer has pieces 2, 3, 4, 5
        var peerPieces = new BitArray(6, false);
        peerPieces[2] = true;
        peerPieces[3] = true;
        peerPieces[4] = true;
        peerPieces[5] = true;

        // Availabilities: piece 4 is the rarest with availability 1
        var availability = new[] { 10, 10, 5, 8, 1, 3 };

        var picked = picker.PickPiece(myPieces, peerPieces, availability, sequential: false);
        Assert.That(picked, Is.EqualTo(4));

        // When piece 4 is completed, next rarest should be piece 5 (avail = 3)
        myPieces[4] = true;
        var pickedNext = picker.PickPiece(myPieces, peerPieces, availability, sequential: false);
        Assert.That(pickedNext, Is.EqualTo(5));
    }

    [Test]
    public void Hybrid_selection_picks_rarest_pieces_outside_sequential_window_when_ratio_allows()
    {
        var fakeRandom = new FakeRandom();
        var picker = new SequentialPiecePicker(random: fakeRandom);

        // 20 pieces. Missing: 0..19. Lookahead window = 5 ([0, 5))
        var myPieces = new BitArray(20, false);

        // Peer has pieces 0, 1 (inside window) and pieces 10, 11 (outside window)
        var peerPieces = new BitArray(20, false);
        peerPieces[0] = true;
        peerPieces[1] = true;
        peerPieces[10] = true;
        peerPieces[11] = true;

        var availability = new int[20];
        Array.Fill(availability, 5);
        availability[10] = 1; // Very rare outside window
        availability[11] = 4;

        // 1. When random roll < rarestFirstRatio, rarest piece outside window is picked (piece 10)
        fakeRandom.DoubleValue = 0.1; // < 0.2 ratio
        var pickedRarest = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.2);
        Assert.That(pickedRarest, Is.EqualTo(10));

        // 2. When random roll >= rarestFirstRatio, sequential piece inside window is picked (piece 0)
        fakeRandom.DoubleValue = 0.5; // >= 0.2 ratio
        var pickedSequential = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.2);
        Assert.That(pickedSequential, Is.EqualTo(0));
    }

    [Test]
    public void Endgame_duplicates_stalled_active_pieces_when_all_window_pieces_are_pending()
    {
        var picker = new SequentialPiecePicker();

        // 5 pieces total. Missing: 0, 1, 2
        var myPieces = new BitArray(5, false);
        myPieces[3] = true;
        myPieces[4] = true;

        var peerPieces = new BitArray(5, true);
        var availability = new[] { 2, 2, 2, 5, 5 };

        // Pieces 0 and 1 are already active/pending
        var activePieces = new HashSet<int> { 0, 1 };

        // When non-active piece 2 is available, picker prefers piece 2
        var pickedNonActive = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: activePieces);
        Assert.That(pickedNonActive, Is.EqualTo(2));

        // When piece 2 is also active, all remaining pieces are active (stalled / endgame mode)
        activePieces.Add(2);
        var pickedEndgame = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: activePieces);

        // Must duplicate the first stalled piece in the active sequential window (piece 0)
        Assert.That(pickedEndgame, Is.EqualTo(0));
    }

    [Test]
    public void Rarest_first_tie_breaking_picks_randomly_between_tied_rarest_candidates()
    {
        var fakeRandom = new FakeRandom();
        var picker = new RarestFirstPiecePicker(fakeRandom);

        var myPieces = new BitArray(5, false);
        var peerPieces = new BitArray(5, true);

        // Pieces 1 and 3 are tied for rarest (avail = 1)
        var availability = new[] { 5, 1, 4, 1, 6 };

        fakeRandom.IntValue = 0;
        var picked0 = picker.PickPiece(myPieces, peerPieces, availability, sequential: false);
        Assert.That(picked0, Is.EqualTo(1));

        fakeRandom.IntValue = 1;
        var picked1 = picker.PickPiece(myPieces, peerPieces, availability, sequential: false);
        Assert.That(picked1, Is.EqualTo(3));
    }

    [Test]
    public void PeerServer_and_PiecePicker_expose_strategy_according_to_torrent_SequentialDownload()
    {
        var piecePicker = new NzbDrone.Core.Peers.PiecePicker.PiecePicker();
        var torrentSequential = new Torrent { SequentialDownload = true };
        var torrentRarest = new Torrent { SequentialDownload = false };

        var pickerForSeq = piecePicker.GetPicker(torrentSequential);
        var pickerForRare = piecePicker.GetPicker(torrentRarest);

        Assert.That(pickerForSeq, Is.InstanceOf<SequentialPiecePicker>());
        Assert.That(pickerForRare, Is.InstanceOf<RarestFirstPiecePicker>());
    }

    [Test]
    public void PickPiece_returns_null_when_all_pieces_are_verified()
    {
        var picker = new SequentialPiecePicker();
        var myPieces = new BitArray(5, true);
        var peerPieces = new BitArray(5, true);
        var availability = new[] { 1, 1, 1, 1, 1 };

        var picked = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(picked, Is.Null);
    }

    [Test]
    public void PickPiece_returns_null_when_peer_has_no_missing_pieces()
    {
        var picker = new SequentialPiecePicker();
        var myPieces = new BitArray(5, false);
        var peerPieces = new BitArray(5, false); // peer has nothing
        var availability = new[] { 1, 1, 1, 1, 1 };

        var picked = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(picked, Is.Null);
    }

    [Test]
    public void Boundary_pieces_prioritized_first_when_firstLastPiecePrio_is_enabled_in_sequential_mode()
    {
        var picker = new SequentialPiecePicker();

        // 20 pieces total. Missing: 0..19.
        var myPieces = new BitArray(20, false);
        var peerPieces = new BitArray(20, true);
        var availability = new int[20];
        Array.Fill(availability, 2);

        // Active pieces is empty. Head (0, 1) and tail (18, 19) should be picked first.
        var picked0 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(picked0, Is.EqualTo(0));

        myPieces[0] = true;
        var picked1 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(picked1, Is.EqualTo(1));

        myPieces[1] = true;
        var pickedTail1 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(pickedTail1, Is.EqualTo(18));

        myPieces[18] = true;
        var pickedTail2 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(pickedTail2, Is.EqualTo(19));

        // When boundary pieces (0, 1, 18, 19) are completed, regular sequential picking resumes at piece 2
        myPieces[19] = true;
        var pickedMiddle = picker.PickPiece(myPieces, peerPieces, availability, sequential: true, lookaheadWindow: 5, rarestFirstRatio: 0.0, activePieces: null, firstLastPiecePrio: true);
        Assert.That(pickedMiddle, Is.EqualTo(2));
    }

    [Test]
    public void Boundary_pieces_prioritized_first_when_firstLastPiecePrio_is_enabled_in_rarest_first_mode()
    {
        var picker = new RarestFirstPiecePicker();

        // 10 pieces. Missing: 0..9.
        var myPieces = new BitArray(10, false);
        var peerPieces = new BitArray(10, true);

        // Piece 5 is extremely rare (avail = 1), middle pieces have low availability
        var availability = new[] { 10, 10, 5, 4, 3, 1, 3, 4, 10, 10 };

        // Even though piece 5 is rarest, firstLastPiecePrio forces boundary pieces (0, 1, 8, 9) first
        var picked0 = picker.PickPiece(myPieces, peerPieces, availability, sequential: false, lookaheadWindow: 20, rarestFirstRatio: 0.2, activePieces: null, firstLastPiecePrio: true);
        Assert.That(picked0, Is.EqualTo(0));

        myPieces[0] = true;
        var picked1 = picker.PickPiece(myPieces, peerPieces, availability, sequential: false, lookaheadWindow: 20, rarestFirstRatio: 0.2, activePieces: null, firstLastPiecePrio: true);
        Assert.That(picked1, Is.EqualTo(1));

        myPieces[1] = true;
        var pickedTail1 = picker.PickPiece(myPieces, peerPieces, availability, sequential: false, lookaheadWindow: 20, rarestFirstRatio: 0.2, activePieces: null, firstLastPiecePrio: true);
        Assert.That(pickedTail1, Is.EqualTo(8));

        myPieces[8] = true;
        var pickedTail2 = picker.PickPiece(myPieces, peerPieces, availability, sequential: false, lookaheadWindow: 20, rarestFirstRatio: 0.2, activePieces: null, firstLastPiecePrio: true);
        Assert.That(pickedTail2, Is.EqualTo(9));

        // Once all boundary pieces are satisfied, rarest piece 5 is picked
        myPieces[9] = true;
        var pickedRarest = picker.PickPiece(myPieces, peerPieces, availability, sequential: false, lookaheadWindow: 20, rarestFirstRatio: 0.2, activePieces: null, firstLastPiecePrio: true);
        Assert.That(pickedRarest, Is.EqualTo(5));
    }

    [Test]
    public void RequestBlock_prioritizes_boundary_active_pieces_when_firstLastPiecePrio_is_enabled()
    {
        var picker = new NzbDrone.Core.Peers.PiecePicker.PiecePicker();

        // Create active pieces 0, 5, 9
        picker.AddActivePiece(0, 16384);
        picker.AddActivePiece(5, 16384);
        picker.AddActivePiece(9, 16384);

        using var ms = new System.IO.MemoryStream();
        var conn = new PeerConnection(ms, "127.0.0.1", 6881)
        {
            PeerChoking = false,
            PeerPieces = Enumerable.Repeat(true, 10).ToArray()
        };

        var torrent = new Torrent
        {
            PieceCount = 10,
            SequentialDownload = false,
            FirstLastPiecePrio = true
        };

        // When firstLastPiecePrio is true, piece 0 (head boundary) must be requested first
        var block = picker.RequestBlock(conn, -1, torrent);
        Assert.That(block, Is.Not.Null);
        Assert.That(block.PieceIndex, Is.EqualTo(0));
    }
}
