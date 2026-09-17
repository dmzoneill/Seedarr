using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers.PiecePicker;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers;

[TestFixture]
public class StreamingPiecePickerTests
{
    [Test]
    public void Urgent_window_prioritized_from_current_playhead_position()
    {
        var picker = new StreamingPiecePicker(urgentWindowSize: 5, lookaheadWindowSize: 20);

        // 100 pieces total. Playhead is at piece 50.
        picker.SetHeadPiece(50);

        var myPieces = new BitArray(100, false);
        for (var i = 0; i < 10; i++)
        {
            myPieces[i] = true;
        }

        var peerPieces = new BitArray(100, true);
        var availability = new int[100];
        Array.Fill(availability, 3);

        // First pick must be piece 50 (urgent head piece)
        var picked1 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(picked1, Is.EqualTo(50));

        // When 50 is downloaded, next should be 51
        myPieces[50] = true;
        var picked2 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(picked2, Is.EqualTo(51));

        // When 51 is downloaded, next should be 52
        myPieces[51] = true;
        var picked3 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(picked3, Is.EqualTo(52));
    }

    [Test]
    public void Shifting_head_position_on_seek_prioritizes_new_urgent_window()
    {
        var picker = new StreamingPiecePicker(urgentWindowSize: 5, lookaheadWindowSize: 20);

        // Start playing at piece 10
        picker.SetHeadPiece(10);

        var myPieces = new BitArray(100, false);
        var peerPieces = new BitArray(100, true);
        var availability = new int[100];
        Array.Fill(availability, 2);

        var pickedInitial = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(pickedInitial, Is.EqualTo(10));

        // User seeks to piece 75
        picker.SetHeadPiece(75);

        // Picker must immediately prioritize piece 75 at new urgent window
        var pickedSeek = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(pickedSeek, Is.EqualTo(75));

        // When 75 is downloaded, next is 76
        myPieces[75] = true;
        var pickedNext = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(pickedNext, Is.EqualTo(76));

        // User seeks backward to piece 25
        picker.SetHeadPiece(25);
        var pickedBackward = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(pickedBackward, Is.EqualTo(25));
    }

    [Test]
    public void Lookahead_buffer_window_picked_when_urgent_window_is_satisfied()
    {
        var picker = new StreamingPiecePicker(urgentWindowSize: 5, lookaheadWindowSize: 20);

        // Playhead at piece 40. Urgent window is [40, 45). Lookahead window is [45, 60).
        picker.SetHeadPiece(40);

        var myPieces = new BitArray(100, false);

        // All urgent pieces 40..44 are already downloaded
        for (var i = 40; i < 45; i++)
        {
            myPieces[i] = true;
        }

        var peerPieces = new BitArray(100, true);
        var availability = new int[100];
        Array.Fill(availability, 2);

        // Next pick must be piece 45 (the first piece in the lookahead buffer)
        var picked = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(picked, Is.EqualTo(45));

        // When 45 is downloaded, next is 46
        myPieces[45] = true;
        var pickedNext = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(pickedNext, Is.EqualTo(46));
    }

    [Test]
    public void SetPlaybackPosition_calculates_head_piece_from_byte_offset_and_piece_length()
    {
        var picker = new StreamingPiecePicker();

        // 10 MB offset with 1 MB pieces = piece 10
        picker.SetPlaybackPosition(torrentId: 1, byteOffset: 10485760, pieceLength: 1048576);
        Assert.That(picker.GetHeadPiece(1), Is.EqualTo(10));

        // Seek to 25 MB = piece 25
        picker.SetPlaybackPosition(torrentId: 1, byteOffset: 26214400, pieceLength: 1048576);
        Assert.That(picker.GetHeadPiece(1), Is.EqualTo(25));
    }

    [Test]
    public void WaitForPieceAsync_returns_true_immediately_when_piece_is_already_available()
    {
        var torrentService = Substitute.For<ITorrentService>();
        var pieceStorage = Substitute.For<IPieceStorage>();

        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "aabbccddeeff00112233445566778899aabbccdd",
            PieceLength = 262144,
            PieceCount = 50
        };

        torrentService.Get(1).Returns(torrent);
        pieceStorage.IsPieceVerified(torrent.InfoHash, 5).Returns(true);

        var streamService = new TorrentStreamService(
            new StreamingPiecePicker(),
            torrentService,
            pieceStorage);

        var result = streamService.WaitForPieceAsync(1, 5, TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task WaitForPieceAsync_completes_and_returns_true_when_piece_is_notified_as_completed()
    {
        var streamService = new TorrentStreamService();

        var waitTask = streamService.WaitForPieceAsync(torrentId: 2, pieceIndex: 15, timeout: TimeSpan.FromSeconds(2));

        Assert.That(waitTask.IsCompleted, Is.False);

        // Simulate piece downloaded & verified
        streamService.NotifyPieceCompleted(torrentId: 2, pieceIndex: 15);

        var result = await waitTask;
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task WaitForPieceAsync_returns_false_on_timeout()
    {
        var streamService = new TorrentStreamService();

        // Wait for uncompleted piece with small timeout
        var result = await streamService.WaitForPieceAsync(torrentId: 3, pieceIndex: 88, timeout: TimeSpan.FromMilliseconds(50));

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task WaitForPieceAsync_returns_false_when_cancellation_is_requested()
    {
        var streamService = new TorrentStreamService();
        using var cts = new CancellationTokenSource();

        var waitTask = streamService.WaitForPieceAsync(
            torrentId: 4,
            pieceIndex: 20,
            timeout: TimeSpan.FromSeconds(5),
            cancellationToken: cts.Token);

        await cts.CancelAsync();

        var result = await waitTask;
        Assert.That(result, Is.False);
    }

    [Test]
    public void NotifyStreamPosition_updates_piece_picker_and_publishes_event()
    {
        var eventAggregator = Substitute.For<IEventAggregator>();
        var torrentService = Substitute.For<ITorrentService>();

        var torrent = new Torrent
        {
            Id = 10,
            PieceLength = 262144,
            PieceCount = 100
        };
        torrentService.Get(10).Returns(torrent);

        var picker = new StreamingPiecePicker();
        var streamService = new TorrentStreamService(
            picker,
            torrentService,
            eventAggregator: eventAggregator);

        // Stream position at 1 MB (offset 1048576, pieceLength 262144 -> head piece 4)
        streamService.NotifyStreamPosition(torrentId: 10, byteOffset: 1048576);

        Assert.That(picker.GetHeadPiece(10), Is.EqualTo(4));
        eventAggregator.Received(1).PublishEvent(Arg.Is<TorrentStreamPlayheadMovedEvent>(e =>
            e.TorrentId == 10 &&
            e.ByteOffset == 1048576 &&
            e.HeadPiece == 4));
    }

    [Test]
    public void Tail_boundary_pieces_prioritized_after_urgent_and_lookahead_are_satisfied()
    {
        var picker = new StreamingPiecePicker(urgentWindowSize: 2, lookaheadWindowSize: 5, tailWindowSize: 2);

        picker.SetHeadPiece(0);

        var myPieces = new BitArray(10, false);
        // Mark urgent (0, 1) and lookahead (2, 3, 4) as downloaded
        for (var i = 0; i < 5; i++)
        {
            myPieces[i] = true;
        }

        var peerPieces = new BitArray(10, true);
        var availability = new int[10];
        Array.Fill(availability, 3);

        // Tail boundary pieces are 8 and 9. Next pick should be 8.
        var picked = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(picked, Is.EqualTo(8));

        myPieces[8] = true;
        var pickedTail2 = picker.PickPiece(myPieces, peerPieces, availability, sequential: true);
        Assert.That(pickedTail2, Is.EqualTo(9));
    }
}
