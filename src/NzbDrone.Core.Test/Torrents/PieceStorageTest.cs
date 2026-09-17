using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class PieceStorageTest
{
    private IBroadcastSignalRMessage _signalRBroadcaster;
    private PieceStorage _pieceStorage;

    [SetUp]
    public void SetUp()
    {
        _signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        _pieceStorage = new PieceStorage(_signalRBroadcaster, TimeSpan.Zero);
    }

    [TearDown]
    public void TearDown()
    {
        _pieceStorage?.Dispose();
    }

    [Test]
    public void MarkPieceVerified_publishes_PieceCompletedMessage_via_SignalR_broadcaster()
    {
        const string hash = "419cb7d4838686ffb08c2a4f4e7c10d3f84898ec";
        const int pieceIndex = 7;
        const long bytesDownloaded = 16384;

        _pieceStorage.MarkPieceVerified(hash, pieceIndex, bytesDownloaded);

        Assert.That(_pieceStorage.IsPieceVerified(hash, pieceIndex), Is.True);
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceCompletedMessage>(m =>
            m.InfoHash == hash &&
            m.PieceIndex == pieceIndex &&
            m.BytesDownloaded == bytesDownloaded &&
            m.Name == "PieceCompleted"));
    }

    [Test]
    public void MarkPiecesVerified_publishes_PieceBatchCompletedMessage_via_SignalR_broadcaster()
    {
        const string hash = "419cb7d4838686ffb08c2a4f4e7c10d3f84898ec";
        var pieceIndexes = new[] { 1, 2, 3 };
        const long bytesDownloaded = 49152;

        _pieceStorage.MarkPiecesVerified(hash, pieceIndexes, bytesDownloaded);

        Assert.That(_pieceStorage.IsPieceVerified(hash, 1), Is.True);
        Assert.That(_pieceStorage.IsPieceVerified(hash, 2), Is.True);
        Assert.That(_pieceStorage.IsPieceVerified(hash, 3), Is.True);

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceBatchCompletedMessage>(m =>
            m.InfoHash == hash &&
            m.PieceIndexes.SequenceEqual(pieceIndexes) &&
            m.BytesDownloaded == bytesDownloaded &&
            m.Name == "PieceBatchCompleted"));
    }

    [Test]
    public void MarkPieceCorrupted_records_corrupted_piece_and_clears_verified()
    {
        const string hash = "abcdef1234567890abcdef1234567890abcdef12";
        _pieceStorage.MarkPieceVerified(hash, 4, 16384);
        Assert.That(_pieceStorage.IsPieceVerified(hash, 4), Is.True);

        _pieceStorage.MarkPieceCorrupted(hash, 4);

        Assert.That(_pieceStorage.IsPieceVerified(hash, 4), Is.False);
        Assert.That(_pieceStorage.IsPieceCorrupted(hash, 4), Is.True);
        Assert.That(_pieceStorage.GetCorruptedPieces(hash), Contains.Item(4));
    }

    [Test]
    public void GetPieceStates_returns_authentic_states()
    {
        const string hash = "test-hash";
        _pieceStorage.MarkPieceVerified(hash, 0, 16384);
        _pieceStorage.MarkPieceVerified(hash, 1, 16384);
        _pieceStorage.MarkPieceCorrupted(hash, 3);

        var activePieces = new HashSet<int> { 2 };
        var states = _pieceStorage.GetPieceStates(hash, 5, activePieces: activePieces);

        Assert.That(states, Is.EqualTo(new[] { 2, 2, 1, 3, 0 }));
    }

    [Test]
    public void Coalescing_batches_rapid_pieces_when_window_is_set()
    {
        const string hash = "batch-test-hash";
        using var coalescingStorage = new PieceStorage(_signalRBroadcaster, TimeSpan.FromMilliseconds(500));

        coalescingStorage.MarkPieceVerified(hash, 1, 1000);
        coalescingStorage.MarkPieceVerified(hash, 2, 2000);
        coalescingStorage.MarkPieceVerified(hash, 3, 3000);

        coalescingStorage.Flush();

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceBatchCompletedMessage>(m =>
            m.InfoHash == hash &&
            m.PieceIndexes.Count == 3 &&
            m.BytesDownloaded == 6000));
    }
}
