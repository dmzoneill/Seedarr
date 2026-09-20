using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.MultiTracker;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class TorrentRecheckServiceTest
{
    private ITorrentRepository _torrentRepository;
    private ITorrentFileService _torrentFileService;
    private IPieceStorage _pieceStorage;
    private IPieceVerificationService _pieceVerificationService;
    private IMultiFilePieceStorage _multiFilePieceStorage;
    private ITorrentStateMachine _stateMachine;
    private IEventAggregator _eventAggregator;
    private IFastResumeService _fastResumeService;
    private TorrentRecheckService _service;

    [SetUp]
    public void SetUp()
    {
        _torrentRepository = Substitute.For<ITorrentRepository>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _pieceStorage = Substitute.For<IPieceStorage>();
        _pieceVerificationService = Substitute.For<IPieceVerificationService>();
        _multiFilePieceStorage = Substitute.For<IMultiFilePieceStorage>();
        _stateMachine = Substitute.For<ITorrentStateMachine>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _fastResumeService = Substitute.For<IFastResumeService>();

        _service = new TorrentRecheckService(
            _torrentRepository,
            _torrentFileService,
            _pieceStorage,
            _pieceVerificationService,
            _multiFilePieceStorage,
            _stateMachine,
            _eventAggregator,
            null,
            _fastResumeService);
    }

    [Test]
    public void Recheck_suspends_active_transfer_state_and_coordinates_with_state_machine()
    {
        var torrent = new Torrent
        {
            Id = 1,
            InfoHash = "hash123",
            Name = "Active Torrent",
            Active = true,
            UploadSpeed = 524288,
            DownloadSpeed = 1048576,
            Status = TorrentStatus.Downloading,
            PieceCount = 2,
            TotalSize = 2000
        };

        _torrentRepository.Get(1).Returns(torrent);
        _stateMachine.When(sm => sm.TransitionToChecking(Arg.Any<Torrent>()))
            .Do(callInfo =>
            {
                var t = callInfo.Arg<Torrent>();
                Assert.That(t.Active, Is.False, "Active flag should be false when TransitionToChecking is called");
                Assert.That(t.UploadSpeed, Is.EqualTo(0), "Upload speed should be 0 during recheck");
                Assert.That(t.DownloadSpeed, Is.EqualTo(0), "Download speed should be 0 during recheck");
                t.Status = TorrentStatus.Checking;
            });

        _stateMachine.TransitionFromChecking(Arg.Any<Torrent>())
            .Returns(TorrentStatus.Downloading);

        _service.Recheck(torrent);

        _stateMachine.Received(1).TransitionToChecking(torrent);
        Assert.That(torrent.Active, Is.False);
        Assert.That(torrent.UploadSpeed, Is.EqualTo(0));
        Assert.That(torrent.DownloadSpeed, Is.EqualTo(0));
    }

    [Test]
    public void Recheck_resumes_to_seeding_when_all_pieces_verified()
    {
        var torrent = new Torrent
        {
            Id = 2,
            InfoHash = "hash-all-valid",
            Name = "Complete Torrent",
            Active = true,
            UploadSpeed = 1000,
            DownloadSpeed = 1000,
            Status = TorrentStatus.Downloading,
            PieceCount = 3,
            PieceLength = 1000,
            TotalSize = 3000
        };

        var pieceHashes = new byte[3 * 20];
        Array.Fill(pieceHashes, (byte)0xAB);

        _pieceVerificationService.VerifyPieceFromStorage(
            torrent,
            Arg.Any<IList<TorrentFile>>(),
            Arg.Any<int>(),
            Arg.Any<byte[]>(),
            Arg.Any<IMultiFilePieceStorage>(),
            Arg.Any<string>()).Returns(true);

        _stateMachine.TransitionFromChecking(Arg.Any<Torrent>())
            .Returns(callInfo =>
            {
                var t = callInfo.Arg<Torrent>();
                t.Status = TorrentStatus.Seeding;
                return TorrentStatus.Seeding;
            });

        var result = _service.Recheck(torrent, pieceHashes);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Progress, Is.EqualTo(1.0));
        Assert.That(result.Status, Is.EqualTo(TorrentStatus.Seeding));
        _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.Length == 3 && b.All(x => x)));
        _stateMachine.Received(1).TransitionFromChecking(torrent);
        _torrentRepository.Received().Update(torrent);
        _fastResumeService.Received(1).SaveFastResume(torrent);
    }

    [Test]
    public void Recheck_resumes_to_downloading_when_partial_pieces_verified()
    {
        var torrent = new Torrent
        {
            Id = 3,
            InfoHash = "hash-partial",
            Name = "Partial Torrent",
            Status = TorrentStatus.Checking,
            PieceCount = 4,
            PieceLength = 1000,
            TotalSize = 4000
        };

        var pieceHashes = new byte[4 * 20];

        _pieceVerificationService.VerifyPieceFromStorage(
            torrent,
            Arg.Any<IList<TorrentFile>>(),
            Arg.Is<int>(i => i < 2),
            Arg.Any<byte[]>(),
            Arg.Any<IMultiFilePieceStorage>(),
            Arg.Any<string>()).Returns(true);

        _pieceVerificationService.VerifyPieceFromStorage(
            torrent,
            Arg.Any<IList<TorrentFile>>(),
            Arg.Is<int>(i => i >= 2),
            Arg.Any<byte[]>(),
            Arg.Any<IMultiFilePieceStorage>(),
            Arg.Any<string>()).Returns(false);

        _stateMachine.TransitionFromChecking(Arg.Any<Torrent>())
            .Returns(callInfo =>
            {
                var t = callInfo.Arg<Torrent>();
                t.Status = TorrentStatus.Downloading;
                return TorrentStatus.Downloading;
            });

        var result = _service.Recheck(torrent, pieceHashes);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Progress, Is.EqualTo(0.5));
        Assert.That(result.Status, Is.EqualTo(TorrentStatus.Downloading));
        _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b[0] && b[1] && !b[2] && !b[3]));
    }

    [Test]
    public void Recheck_returns_null_when_torrent_not_found()
    {
        _torrentRepository.Get(999).Returns((Torrent)null);

        var result = _service.Recheck(999);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void PeerServer_rejects_peer_request_when_torrent_in_checking_status()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var connectionManager = Substitute.For<IConnectionManager>();
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var multiTracker = Substitute.For<IMultiTrackerManager>();
        var fastExtension = Substitute.For<IFastExtensionHandler>();

        configService.MaxGlobalConnections.Returns(200);
        configService.ListeningPort.Returns(0);
        configService.EncryptionMode.Returns("enabled");
        configService.PeerIdleChance.Returns(0.0);

        var server = new PeerServer(
            configService,
            torrentService,
            connectionManager,
            peerDiscovery,
            multiTracker,
            fastExtensionHandler: fastExtension);

        var torrent = new Torrent
        {
            Id = 10,
            InfoHash = "hash-checking-test",
            Name = "Checking Torrent",
            Status = TorrentStatus.Checking,
            PieceCount = 10,
            PieceLength = 16384
        };

        var connection = new PeerConnection(new MemoryStream(), "127.0.0.1", 6881)
        {
            MatchedTorrent = torrent,
            SupportsFastExtension = true,
            AmChoking = false
        };

        var requestPayload = new byte[12];
        // Piece index 0, begin 0, length 16384
        requestPayload[3] = 0;
        requestPayload[7] = 0;
        requestPayload[8] = 0;
        requestPayload[9] = 0;
        requestPayload[10] = 0x40;
        requestPayload[11] = 0x00;

        var rejectMessage = new PeerMessage
        {
            Type = PeerMessageType.RejectRequest,
            Payload = requestPayload
        };

        fastExtension.BuildRejectForRequest(Arg.Any<byte[]>()).Returns(rejectMessage);

        var initialUploaded = connection.BytesUploaded;

        server.HandleMessage(
            connection,
            new PeerMessage { Type = PeerMessageType.Request, Payload = requestPayload },
            torrent);

        // Verify request was rejected and not serviced
        Assert.That(connection.BytesUploaded, Is.EqualTo(initialUploaded), "No bytes should be uploaded for torrent in checking status");
        fastExtension.Received(1).BuildRejectForRequest(requestPayload);
    }

    [Test]
    public void PeerServer_drops_incoming_piece_block_when_torrent_in_checking_status()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var connectionManager = Substitute.For<IConnectionManager>();
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var multiTracker = Substitute.For<IMultiTrackerManager>();

        var server = new PeerServer(
            configService,
            torrentService,
            connectionManager,
            peerDiscovery,
            multiTracker);

        var torrent = new Torrent
        {
            Id = 11,
            InfoHash = "hash-drop-piece",
            Name = "Checking Torrent",
            Status = TorrentStatus.Checking,
            PieceCount = 10
        };

        var connection = new PeerConnection(new MemoryStream(), "127.0.0.1", 6881)
        {
            MatchedTorrent = torrent
        };

        var piecePayload = new byte[8 + 16384];

        Assert.DoesNotThrow(() =>
        {
            server.HandleMessage(
                connection,
                new PeerMessage { Type = PeerMessageType.Piece, Payload = piecePayload },
                torrent);
        });
    }

    [Test]
    public void PeerServer_sends_empty_availability_during_checking_and_resumes_after_completion()
    {
        var configService = Substitute.For<IConfigService>();
        var torrentService = Substitute.For<ITorrentService>();
        var connectionManager = Substitute.For<IConnectionManager>();
        var peerDiscovery = Substitute.For<IPeerDiscoveryService>();
        var multiTracker = Substitute.For<IMultiTrackerManager>();
        var fastExtension = Substitute.For<IFastExtensionHandler>();

        var server = new PeerServer(
            configService,
            torrentService,
            connectionManager,
            peerDiscovery,
            multiTracker,
            fastExtensionHandler: fastExtension);

        var torrent = new Torrent
        {
            Id = 12,
            InfoHash = "hash-avail-test",
            Name = "Availability Torrent",
            Status = TorrentStatus.Checking,
            PieceCount = 8
        };

        var connection = new PeerConnection(new MemoryStream(), "127.0.0.1", 6881)
        {
            MatchedTorrent = torrent,
            SupportsFastExtension = true
        };

        var haveNoneMsg = new PeerMessage { Type = PeerMessageType.HaveNone };
        fastExtension.SerializeHaveNone().Returns(haveNoneMsg);

        server.SendInitialAvailability(connection, torrent);

        fastExtension.Received(1).SerializeHaveNone();

        // Now test resumption when recheck finishes
        torrent.Status = TorrentStatus.Seeding;
        torrent.Progress = 1.0;

        connectionManager.GetConnections(torrent.InfoHash).Returns(new List<PeerConnection> { connection });

        server.Handle(new TorrentStatusChangedEvent(torrent, TorrentStatus.Checking, TorrentStatus.Seeding));

        // When recheck finishes, availability is updated for connected swarm peers
        // When recheck finishes, availability is updated for connected swarm peers
        fastExtension.Received().SendHaveAllOrBitfield(connection, 8, true, false, Arg.Any<byte[]>());
    }

    [Test]
    public void QueueRecheck_sets_status_to_QueuedForChecking_and_returns_torrent()
    {
        var torrent = new Torrent
        {
            Id = 20,
            Name = "Queued Torrent",
            Status = TorrentStatus.Paused,
            PieceCount = 2,
            TotalSize = 2000
        };

        _torrentRepository.Get(20).Returns(torrent);

        var result = _service.QueueRecheck(20);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Status, Is.EqualTo(TorrentStatus.QueuedForChecking));
        _torrentRepository.Received().Update(Arg.Is<Torrent>(t => t.Id == 20 && t.Status == TorrentStatus.QueuedForChecking));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e => e.Torrent.Id == 20 && e.NewStatus == TorrentStatus.QueuedForChecking));
    }

    [Test]
    public async System.Threading.Tasks.Task RecheckAsync_publishes_TorrentRecheckProgressEvent_during_piece_verification()
    {
        var torrent = new Torrent
        {
            Id = 21,
            Name = "Progress Torrent",
            Status = TorrentStatus.Downloading,
            PieceCount = 4,
            TotalSize = 4000
        };

        _torrentRepository.Get(21).Returns(torrent);

        var result = await _service.RecheckAsync(torrent);

        Assert.That(result, Is.Not.Null);
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentRecheckProgressEvent>(e => e.TorrentId == 21 && e.TotalPieces == 4));
    }

    [Test]
    public void RecheckAsync_respects_cancellation_token()
    {
        var torrent = new Torrent
        {
            Id = 22,
            Name = "Cancel Torrent",
            Status = TorrentStatus.Downloading,
            PieceCount = 10,
            TotalSize = 10000
        };

        _torrentRepository.Get(22).Returns(torrent);

        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<System.OperationCanceledException>(async () =>
        {
            await _service.RecheckAsync(torrent, null, cts.Token);
        });

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Paused));
    }

    [Test]
    public async System.Threading.Tasks.Task RecheckAsync_broadcasts_progress_via_signalr_broadcaster()
    {
        var signalR = Substitute.For<NzbDrone.SignalR.IBroadcastSignalRMessage>();
        var serviceWithSignalR = new TorrentRecheckService(
            _torrentRepository,
            _torrentFileService,
            _pieceStorage,
            _pieceVerificationService,
            _multiFilePieceStorage,
            _stateMachine,
            _eventAggregator,
            null,
            _fastResumeService,
            signalR);

        var torrent = new Torrent
        {
            Id = 23,
            Name = "SignalR Torrent",
            Status = TorrentStatus.Downloading,
            PieceCount = 2,
            TotalSize = 2000
        };

        _torrentRepository.Get(23).Returns(torrent);

        await serviceWithSignalR.RecheckAsync(torrent);

        signalR.Received().BroadcastMessage(Arg.Is<NzbDrone.SignalR.SignalRMessage>(m => m.Name == "TorrentRecheckProgress"));
    }
}
