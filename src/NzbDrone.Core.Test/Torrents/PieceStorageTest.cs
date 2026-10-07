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
    public void MarkPieceVerified_fans_out_to_torrent_group_when_torrent_service_resolves_hash()
    {
        const string hash = "419cb7d4838686ffb08c2a4f4e7c10d3f84898ec";
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetByInfoHash(hash).Returns(new Torrent { Id = 77, InfoHash = hash });

        using var storage = new PieceStorage(_signalRBroadcaster, TimeSpan.Zero, torrentService);
        storage.MarkPieceVerified(hash, 2, 16384);

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceCompletedMessage>(m => m.TorrentId == 77));
        _signalRBroadcaster.Received(1).BroadcastToChannel("torrents", Arg.Any<PieceCompletedMessage>());
        _signalRBroadcaster.Received(1).BroadcastToTorrent(77, Arg.Any<PieceCompletedMessage>());
    }

    [Test]
    public void SetVerifiedPieces_publishes_pieceMapUpdated_with_bulk_bitfield_via_SignalR_broadcaster()
    {
        const string hash = "419cb7d4838686ffb08c2a4f4e7c10d3f84898ec";
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetByInfoHash(hash).Returns(new Torrent { Id = 88, InfoHash = hash });
        var bitfield = new[] { true, false, true };

        using var storage = new PieceStorage(_signalRBroadcaster, TimeSpan.Zero, torrentService);
        storage.SetVerifiedPieces(hash, bitfield);

        Assert.That(storage.GetVerifiedPieces(hash), Is.EqualTo(bitfield));
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceMapUpdatedMessage>(m =>
            m.InfoHash == hash &&
            m.TorrentId == 88 &&
            !m.Cleared &&
            m.Name == "pieceMapUpdated"));
    }

    [Test]
    public void SetVerifiedPieces_with_null_publishes_pieceMapUpdated_cleared_via_SignalR_broadcaster()
    {
        const string hash = "419cb7d4838686ffb08c2a4f4e7c10d3f84898ec";
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetByInfoHash(hash).Returns(new Torrent { Id = 89, InfoHash = hash });

        using var storage = new PieceStorage(_signalRBroadcaster, TimeSpan.Zero, torrentService);
        storage.SetVerifiedPieces(hash, new[] { true });
        storage.SetVerifiedPieces(hash, null);

        Assert.That(storage.GetVerifiedPieces(hash), Is.Null);
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceMapUpdatedMessage>(m =>
            m.InfoHash == hash &&
            m.TorrentId == 89 &&
            m.Cleared &&
            m.Name == "pieceMapUpdated"));
    }

    [Test]
    public void Clear_publishes_pieceMapUpdated_with_cleared_marker_via_SignalR_broadcaster()
    {
        const string hash = "419cb7d4838686ffb08c2a4f4e7c10d3f84898ec";
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetByInfoHash(hash).Returns(new Torrent { Id = 55, InfoHash = hash });

        using var storage = new PieceStorage(_signalRBroadcaster, TimeSpan.Zero, torrentService);
        storage.MarkPieceVerified(hash, 3, 16384);
        storage.Clear(hash);

        Assert.That(storage.GetVerifiedPieces(hash), Is.Null);
        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceMapUpdatedMessage>(m =>
            m.InfoHash == hash &&
            m.TorrentId == 55 &&
            m.Cleared &&
            m.Name == "pieceMapUpdated"));
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
    public void Coalescing_deduplicates_repeated_piece_index_before_flush()
    {
        const string hash = "duplicate-index-coalesce-hash";
        const int pieceIndex = 5;
        const long bytesPerCall = 1000;
        using var coalescingStorage = new PieceStorage(_signalRBroadcaster, TimeSpan.FromMilliseconds(500));

        coalescingStorage.MarkPieceVerified(hash, pieceIndex, bytesPerCall);
        coalescingStorage.MarkPieceVerified(hash, pieceIndex, bytesPerCall);
        coalescingStorage.MarkPieceVerified(hash, pieceIndex, bytesPerCall);

        coalescingStorage.Flush();

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceCompletedMessage>(m =>
            m.InfoHash == hash &&
            m.PieceIndex == pieceIndex &&
            m.BytesDownloaded == bytesPerCall));
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

    [Test]
    public void Coalescing_batches_MarkPiecesVerified_when_window_is_set()
    {
        const string hash = "bulk-coalesce-hash";
        var pieceIndexes = new[] { 1, 2, 3 };
        using var coalescingStorage = new PieceStorage(_signalRBroadcaster, TimeSpan.FromMilliseconds(500));

        coalescingStorage.MarkPiecesVerified(hash, pieceIndexes, 6000);

        _signalRBroadcaster.DidNotReceive().BroadcastMessage(Arg.Any<SignalRMessage>());
        Assert.That(coalescingStorage.PendingBatchCount, Is.EqualTo(1));

        coalescingStorage.Flush();

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceBatchCompletedMessage>(m =>
            m.InfoHash == hash &&
            m.PieceIndexes.SequenceEqual(pieceIndexes) &&
            m.BytesDownloaded == 6000));
    }

    [Test]
    public void Coalescing_MarkPiecesVerified_merges_with_pending_MarkPieceVerified_batch()
    {
        const string hash = "mixed-bulk-coalesce-hash";
        using var coalescingStorage = new PieceStorage(_signalRBroadcaster, TimeSpan.FromMilliseconds(500));

        coalescingStorage.MarkPieceVerified(hash, 1, 1000);
        coalescingStorage.MarkPiecesVerified(hash, new[] { 2, 3 }, 5000);

        coalescingStorage.Flush();

        _signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<PieceBatchCompletedMessage>(m =>
            m.InfoHash == hash &&
            m.PieceIndexes.Count == 3 &&
            m.BytesDownloaded == 6000));
    }

    [Test]
    public void Flush_prunes_empty_pending_batches()
    {
        const string hash = "prune-test-hash";
        using var storage = new PieceStorage(_signalRBroadcaster, TimeSpan.FromMilliseconds(500));

        storage.MarkPieceVerified(hash, 1, 1000);
        storage.MarkPieceVerified(hash, 2, 2000);

        Assert.That(storage.PendingBatchCount, Is.EqualTo(1));

        storage.Flush();

        Assert.That(storage.PendingBatchCount, Is.EqualTo(0));

        // Verify that queuing new pieces after flush recreates batch and can be flushed again
        storage.MarkPieceVerified(hash, 3, 3000);
        Assert.That(storage.PendingBatchCount, Is.EqualTo(1));

        storage.Flush();
        Assert.That(storage.PendingBatchCount, Is.EqualTo(0));
    }

    [Test]
    public void CloseHandles_prunes_pending_batches()
    {
        const string hash = "close-handles-test-hash";
        using var storage = new PieceStorage(_signalRBroadcaster, TimeSpan.FromMilliseconds(500));

        storage.MarkPieceVerified(hash, 1, 1000);
        Assert.That(storage.PendingBatchCount, Is.EqualTo(1));

        storage.CloseHandles(hash);
        Assert.That(storage.PendingBatchCount, Is.EqualTo(0));
    }

    [Test]
    public void Flush_drains_batches_queued_during_flush_broadcast()
    {
        const string hash = "flush-drain-during-broadcast-hash";
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        using var storage = new PieceStorage(broadcaster, TimeSpan.FromMilliseconds(500));
        var chainedPieceQueued = false;

        broadcaster.When(b => b.BroadcastMessage(Arg.Any<SignalRMessage>())).Do(_ =>
        {
            if (chainedPieceQueued)
            {
                return;
            }

            chainedPieceQueued = true;
            storage.MarkPieceVerified(hash, 2, 2000);
        });

        storage.MarkPieceVerified(hash, 1, 1000);
        storage.Flush();

        Assert.That(storage.PendingBatchCount, Is.EqualTo(0));
        broadcaster.Received(2).BroadcastMessage(Arg.Any<SignalRMessage>());
    }

    [Test]
    public void Dispose_flushes_pending_coalesced_batches()
    {
        const string hash = "dispose-flush-hash";
        var broadcaster = Substitute.For<IBroadcastSignalRMessage>();
        var storage = new PieceStorage(broadcaster, TimeSpan.FromMilliseconds(500));

        storage.MarkPieceVerified(hash, 1, 1000);
        storage.Dispose();

        broadcaster.Received(1).BroadcastMessage(Arg.Is<PieceCompletedMessage>(m =>
            m.InfoHash == hash && m.PieceIndex == 1 && m.BytesDownloaded == 1000));
    }

    [Test]
    public void MultiFilePieceStorage_ResolveFilePath_should_throw_SecurityException_on_path_traversal()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "test_base");
        Assert.Throws<System.Security.SecurityException>(() =>
            MultiFilePieceStorage.ResolveFilePath(baseDir, "../../../evil.sh"));
    }

    [Test]
    public void MultiFilePieceStorage_ResolveFilePath_should_throw_SecurityException_on_rooted_path()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "test_base");
        var rooted = OperatingSystem.IsWindows() ? "C:\\Windows\\System32\\calc.exe" : "/etc/shadow";
        Assert.Throws<System.Security.SecurityException>(() =>
            MultiFilePieceStorage.ResolveFilePath(baseDir, rooted));
    }

    [Test]
    public void MultiFilePieceStorage_ResolveFilePath_should_resolve_safe_subpath()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "test_base");
        var resolved = MultiFilePieceStorage.ResolveFilePath(baseDir, "movies/video.mkv");
        var expected = Path.GetFullPath(Path.Combine(baseDir, "movies", "video.mkv"));
        Assert.That(resolved, Is.EqualTo(expected));
    }
}
