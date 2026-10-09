using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Seeding;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers.MultiTracker;
using NzbDrone.SignalR;

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
    public void Recheck_does_not_trust_progress_when_files_and_piece_hashes_missing()
    {
        var torrent = new Torrent
        {
            Id = 62,
            InfoHash = "hash-no-metadata",
            Name = "Stale Progress Torrent",
            Status = TorrentStatus.Downloading,
            Progress = 1.0,
            PieceCount = 0,
            TotalSize = 1000,
            SourcePath = null
        };

        _torrentFileService.GetByTorrentId(62).Returns(new List<TorrentFile>());

        _stateMachine.TransitionFromChecking(Arg.Any<Torrent>())
            .Returns(callInfo =>
            {
                var t = callInfo.Arg<Torrent>();
                t.Status = TorrentStatus.Downloading;
                return TorrentStatus.Downloading;
            });

        var result = _service.Recheck(torrent);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Progress, Is.EqualTo(0.0));
        Assert.That(result.Status, Is.EqualTo(TorrentStatus.Downloading));
        _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.Length == 1 && !b[0]));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentHashCheckCompletedEvent>(e => e.Torrent == torrent && !e.IsSuccessful));
    }

    [Test]
    public void Recheck_presence_fallback_finds_files_under_torrent_name_subdirectory()
    {
        var saveRoot = Path.Combine(Path.GetTempPath(), "seedarr_recheck_" + Guid.NewGuid().ToString("N"));
        var torrentSubdir = Path.Combine(saveRoot, "My Show");
        Directory.CreateDirectory(torrentSubdir);
        var payloadPath = Path.Combine(torrentSubdir, "episode.mkv");
        File.WriteAllBytes(payloadPath, new byte[1000]);

        try
        {
            var torrent = new Torrent
            {
                Id = 60,
                InfoHash = "hash-presence-subdir",
                Name = "My Show",
                SavePath = saveRoot,
                Status = TorrentStatus.Downloading,
                PieceCount = 1,
                PieceLength = 1000,
                TotalSize = 1000,
                SourcePath = null
            };

            _torrentFileService.GetByTorrentId(60).Returns(new List<TorrentFile>
            {
                new() { TorrentId = 60, Path = "episode.mkv", Size = 1000 }
            });

            _stateMachine.TransitionFromChecking(Arg.Any<Torrent>())
                .Returns(callInfo =>
                {
                    var t = callInfo.Arg<Torrent>();
                    t.Status = TorrentStatus.Seeding;
                    return TorrentStatus.Seeding;
                });

            var result = _service.Recheck(torrent);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Progress, Is.EqualTo(1.0));
            Assert.That(result.Status, Is.EqualTo(TorrentStatus.Seeding));
            _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.Length == 1 && b[0]));
        }
        finally
        {
            try
            {
                if (Directory.Exists(saveRoot))
                {
                    Directory.Delete(saveRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup
            }
        }
    }

    [Test]
    public void Recheck_presence_fallback_skips_bep47_padding_files_missing_on_disk()
    {
        var saveRoot = Path.Combine(Path.GetTempPath(), "seedarr_recheck_pad_" + Guid.NewGuid().ToString("N"));
        var torrentSubdir = Path.Combine(saveRoot, "Padded Show");
        Directory.CreateDirectory(torrentSubdir);
        var payloadPath = Path.Combine(torrentSubdir, "episode.mkv");
        File.WriteAllBytes(payloadPath, new byte[800]);

        try
        {
            var torrent = new Torrent
            {
                Id = 61,
                InfoHash = "hash-presence-padding",
                Name = "Padded Show",
                SavePath = saveRoot,
                Status = TorrentStatus.Downloading,
                PieceCount = 1,
                PieceLength = 1000,
                TotalSize = 1000,
                SourcePath = null
            };

            _torrentFileService.GetByTorrentId(61).Returns(new List<TorrentFile>
            {
                new() { TorrentId = 61, Path = "episode.mkv", Size = 800 },
                new() { TorrentId = 61, Path = ".pad/200", Size = 200, IsPaddingFile = true }
            });

            _stateMachine.TransitionFromChecking(Arg.Any<Torrent>())
                .Returns(callInfo =>
                {
                    var t = callInfo.Arg<Torrent>();
                    t.Status = TorrentStatus.Seeding;
                    return TorrentStatus.Seeding;
                });

            var result = _service.Recheck(torrent);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Progress, Is.EqualTo(1.0));
            Assert.That(result.Status, Is.EqualTo(TorrentStatus.Seeding));
            _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.Length == 1 && b[0]));
        }
        finally
        {
            try
            {
                if (Directory.Exists(saveRoot))
                {
                    Directory.Delete(saveRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup
            }
        }
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
            TotalSize = 3000,
            Downloaded = 9999
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
        Assert.That(result.Downloaded, Is.EqualTo(3000));
        Assert.That(result.Status, Is.EqualTo(TorrentStatus.Seeding));
        _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b.Length == 3 && b.All(x => x)));
        _stateMachine.Received(1).TransitionFromChecking(torrent);
        _torrentRepository.Received().Update(torrent);
        _fastResumeService.Received(1).SaveFastResume(torrent);
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentHashCheckCompletedEvent>(e => e.Torrent == torrent && e.IsSuccessful));
    }

    [Test]
    public void Recheck_uses_torrent_piece_hashes_when_source_path_missing()
    {
        var pieceHashes = new byte[2 * 20];
        Array.Fill(pieceHashes, (byte)0xCD);

        var torrent = new Torrent
        {
            Id = 50,
            InfoHash = "hash-on-entity",
            Name = "Magnet Torrent",
            Status = TorrentStatus.Downloading,
            PieceCount = 2,
            PieceLength = 1000,
            TotalSize = 2000,
            PieceHashes = pieceHashes,
            SourcePath = null
        };

        _torrentFileService.GetByTorrentId(50).Returns(new List<TorrentFile>
        {
            new() { TorrentId = 50, Path = "file.bin", Size = 2000 }
        });

        _pieceVerificationService.VerifyPieceFromStorage(
            torrent,
            Arg.Any<IList<TorrentFile>>(),
            Arg.Any<int>(),
            Arg.Any<byte[]>(),
            Arg.Any<IMultiFilePieceStorage>(),
            Arg.Any<string>()).Returns(true);

        _stateMachine.TransitionFromChecking(Arg.Any<Torrent>()).Returns(TorrentStatus.Seeding);

        _service.Recheck(torrent);

        _pieceVerificationService.Received(2).VerifyPieceFromStorage(
            torrent,
            Arg.Any<IList<TorrentFile>>(),
            Arg.Any<int>(),
            Arg.Any<byte[]>(),
            Arg.Any<IMultiFilePieceStorage>(),
            Arg.Any<string>());
    }

    [Test]
    public void Recheck_uses_piece_hash_cache_when_repository_entity_lacks_hashes()
    {
        var pieceHashes = new byte[20];
        Array.Fill(pieceHashes, (byte)0xEF);

        TorrentService.PopulatePieceHashes(new Torrent
        {
            Id = 51,
            InfoHash = "cached-hash",
            PieceHashes = pieceHashes
        });

        var torrent = new Torrent
        {
            Id = 51,
            InfoHash = "cached-hash",
            Name = "Cached Hash Torrent",
            Status = TorrentStatus.Downloading,
            PieceCount = 1,
            PieceLength = 1000,
            TotalSize = 1000
        };

        _torrentFileService.GetByTorrentId(51).Returns(new List<TorrentFile>
        {
            new() { TorrentId = 51, Path = "file.bin", Size = 1000 }
        });

        _pieceVerificationService.VerifyPieceFromStorage(
            torrent,
            Arg.Any<IList<TorrentFile>>(),
            Arg.Any<int>(),
            Arg.Any<byte[]>(),
            Arg.Any<IMultiFilePieceStorage>(),
            Arg.Any<string>()).Returns(true);

        _stateMachine.TransitionFromChecking(Arg.Any<Torrent>()).Returns(TorrentStatus.Seeding);

        _service.Recheck(torrent);

        _pieceVerificationService.Received(1).VerifyPieceFromStorage(
            torrent,
            Arg.Any<IList<TorrentFile>>(),
            0,
            Arg.Is<byte[]>(h => h.Length == 20 && h[0] == 0xEF),
            Arg.Any<IMultiFilePieceStorage>(),
            Arg.Any<string>());
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
            TotalSize = 4000,
            Downloaded = 4000
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
        Assert.That(result.Downloaded, Is.EqualTo(2000));
        Assert.That(result.Status, Is.EqualTo(TorrentStatus.Downloading));
        _pieceStorage.Received(1).SetVerifiedPieces(torrent.InfoHash, Arg.Is<bool[]>(b => b[0] && b[1] && !b[2] && !b[3]));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentHashCheckCompletedEvent>(e => e.Torrent == torrent && !e.IsSuccessful));
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
    public void CancelRecheck_restores_prior_status_when_recheck_still_queued()
    {
        var torrent = new Torrent
        {
            Id = 25,
            Name = "Cancel Queued Torrent",
            Status = TorrentStatus.Downloading,
            PieceCount = 1,
            TotalSize = 1000
        };

        _torrentRepository.Get(25).Returns(torrent);

        _service.QueueRecheck(25);
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.QueuedForChecking));

        _service.CancelRecheck(25);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
        _torrentRepository.Received(2).Update(Arg.Is<Torrent>(t => t.Id == 25));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e =>
            e.Torrent.Id == 25 && e.OldStatus == TorrentStatus.QueuedForChecking && e.NewStatus == TorrentStatus.Downloading));
        _eventAggregator.Received().PublishEvent(Arg.Is<NzbDrone.Core.Datastore.Events.ModelEvent<Torrent>>(e =>
            e.Model.Id == 25 && e.Action == NzbDrone.Core.Datastore.ModelAction.Updated));
    }

    [Test]
    public async System.Threading.Tasks.Task CancelRecheck_restores_status_when_queue_processor_skips_cancelled_id()
    {
        var torrent = new Torrent
        {
            Id = 26,
            Name = "Deferred Cancel Torrent",
            Status = TorrentStatus.Seeding,
            PieceCount = 1,
            TotalSize = 1000
        };

        _torrentRepository.Get(26).Returns(torrent);

        _service.QueueRecheck(26);
        _service.CancelRecheck(26);

        await System.Threading.Tasks.Task.Delay(100);

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
    }

    [Test]
    public async System.Threading.Tasks.Task CancelRecheck_broadcasts_TorrentUpdated_via_signalr_when_queued()
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
            Id = 27,
            Name = "SignalR Cancel Torrent",
            Status = TorrentStatus.Paused,
            PieceCount = 1,
            TotalSize = 1000
        };

        _torrentRepository.Get(27).Returns(torrent);

        serviceWithSignalR.QueueRecheck(27);
        serviceWithSignalR.CancelRecheck(27);

        signalR.Received().BroadcastMessage(Arg.Is<NzbDrone.SignalR.SignalRMessage>(m =>
            m.Name == "TorrentUpdated" && m.Action == NzbDrone.Core.Datastore.ModelAction.Updated));
    }

    [Test]
    public async System.Threading.Tasks.Task CancelRecheck_restores_status_and_broadcasts_when_waiting_on_concurrency_semaphore()
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

        var blockingGate = new ManualResetEventSlim(false);
        var blockingVerifyStarted = new ManualResetEventSlim(false);
        var blockingTorrent = new Torrent
        {
            Id = 28,
            Name = "Blocking Recheck",
            Status = TorrentStatus.Downloading,
            PieceCount = 1,
            PieceHashes = new byte[20],
            TotalSize = 1000
        };
        var waitingTorrent = new Torrent
        {
            Id = 29,
            Name = "Waiting Recheck",
            Status = TorrentStatus.Seeding,
            PieceCount = 1,
            PieceHashes = new byte[20],
            TotalSize = 1000
        };

        _torrentRepository.Get(28).Returns(blockingTorrent);
        _torrentRepository.Get(29).Returns(waitingTorrent);

        _stateMachine.When(sm => sm.TransitionToChecking(Arg.Any<Torrent>()))
            .Do(ci => ci.Arg<Torrent>().Status = TorrentStatus.Checking);
        _stateMachine.TransitionFromChecking(Arg.Any<Torrent>())
            .Returns(ci =>
            {
                ci.Arg<Torrent>().Status = TorrentStatus.Seeding;
                return TorrentStatus.Seeding;
            });

        _pieceVerificationService.VerifyPieceFromStorage(
                Arg.Any<Torrent>(),
                Arg.Any<IList<TorrentFile>>(),
                Arg.Any<int>(),
                Arg.Any<byte[]>(),
                Arg.Any<IMultiFilePieceStorage>(),
                Arg.Any<string>())
            .Returns(_ =>
            {
                blockingVerifyStarted.Set();
                if (!blockingGate.Wait(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("Blocking recheck did not release within 30s");
                }

                return true;
            });

        try
        {
            var blockingRecheck = serviceWithSignalR.RecheckAsync(blockingTorrent);
            Assert.That(
                SpinWait.SpinUntil(() => blockingVerifyStarted.IsSet, TimeSpan.FromSeconds(5)),
                Is.True,
                "Expected blocking recheck to enter piece verification");

            serviceWithSignalR.QueueRecheck(29);
            SpinWait.SpinUntil(() => waitingTorrent.Status == TorrentStatus.QueuedForChecking, TimeSpan.FromSeconds(2));
            Assert.That(waitingTorrent.Status, Is.EqualTo(TorrentStatus.QueuedForChecking));

            serviceWithSignalR.CancelRecheck(29);

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline && waitingTorrent.Status == TorrentStatus.QueuedForChecking)
            {
                await System.Threading.Tasks.Task.Delay(10);
            }

            Assert.That(waitingTorrent.Status, Is.EqualTo(TorrentStatus.Seeding));
            signalR.Received().BroadcastMessage(Arg.Is<NzbDrone.SignalR.SignalRMessage>(m =>
                m.Name == "TorrentUpdated" && m.Action == NzbDrone.Core.Datastore.ModelAction.Updated));

            blockingGate.Set();
            await blockingRecheck;
        }
        finally
        {
            blockingGate.Set();
        }
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
    public async System.Threading.Tasks.Task ProcessQueueAsync_processes_tail_enqueue_when_spawn_races_lock_release()
    {
        const int firstId = 40;
        const int tailId = 41;
        var first = new Torrent
        {
            Id = firstId,
            Name = "First Queued Torrent",
            Status = TorrentStatus.Paused,
            PieceCount = 1,
            TotalSize = 1000
        };
        var tail = new Torrent
        {
            Id = tailId,
            Name = "Tail Queued Torrent",
            Status = TorrentStatus.Paused,
            PieceCount = 1,
            TotalSize = 1000
        };

        _torrentRepository.Get(firstId).Returns(first);
        _torrentRepository.Get(tailId).Returns(tail);

        _pieceVerificationService.VerifyPieceFromStorage(
                Arg.Any<Torrent>(),
                Arg.Any<IList<TorrentFile>>(),
                Arg.Any<int>(),
                Arg.Any<byte[]>(),
                Arg.Any<IMultiFilePieceStorage>(),
                Arg.Any<string>())
            .Returns(true);
        _stateMachine.When(sm => sm.TransitionToChecking(Arg.Any<Torrent>()))
            .Do(ci => ci.Arg<Torrent>().Status = TorrentStatus.Checking);
        _stateMachine.TransitionFromChecking(Arg.Any<Torrent>())
            .Returns(ci =>
            {
                ci.Arg<Torrent>().Status = TorrentStatus.Seeding;
                return TorrentStatus.Seeding;
            });

        var activeRechecks = typeof(TorrentRecheckService)
            .GetField("_activeRechecks", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(_service) as ConcurrentDictionary<int, CancellationTokenSource>;

        _service.QueueRecheck(firstId);

        var racer = System.Threading.Tasks.Task.Run(() =>
        {
            Assert.That(
                SpinWait.SpinUntil(() => activeRechecks!.ContainsKey(firstId), TimeSpan.FromSeconds(5)),
                Is.True,
                "Expected first queued recheck to become active");
            Assert.That(
                SpinWait.SpinUntil(() => !activeRechecks!.ContainsKey(firstId), TimeSpan.FromSeconds(5)),
                Is.True,
                "Expected first queued recheck to finish");
            for (var i = 0; i < 10_000; i++)
            {
                _service.QueueRecheck(tailId);
                if (tail.Status == TorrentStatus.Seeding)
                {
                    return;
                }
            }
        });

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && tail.Status != TorrentStatus.Seeding)
        {
            await System.Threading.Tasks.Task.Delay(5);
        }

        await racer;
        Assert.That(tail.Status, Is.EqualTo(TorrentStatus.Seeding));
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
            await serviceWithSignalR.RecheckAsync(torrent, null, cts.Token);
        });

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Paused));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e =>
            e.Torrent == torrent && e.OldStatus == TorrentStatus.Downloading && e.NewStatus == TorrentStatus.Paused));
        _eventAggregator.Received().PublishEvent(Arg.Is<NzbDrone.Core.Datastore.Events.ModelEvent<Torrent>>(e => e.Model == torrent && e.Action == NzbDrone.Core.Datastore.ModelAction.Updated));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentHashCheckCompletedEvent>(e => e.Torrent == torrent && !e.IsSuccessful));
        signalR.Received().BroadcastMessage(Arg.Is<NzbDrone.SignalR.SignalRMessage>(m =>
            m.Name == "TorrentUpdated" && m.Action == NzbDrone.Core.Datastore.ModelAction.Updated));
    }

    [Test]
    public void Recheck_completion_keeps_paused_when_user_paused_during_checking()
    {
        var torrent = new Torrent
        {
            Id = 24,
            InfoHash = "hash-paused-recheck",
            Name = "Paused During Recheck",
            Status = TorrentStatus.Downloading,
            PieceCount = 2,
            TotalSize = 2000
        };

        var persistedTorrent = new Torrent
        {
            Id = 24,
            InfoHash = torrent.InfoHash,
            Name = torrent.Name,
            Status = TorrentStatus.Paused,
            PieceCount = 2,
            TotalSize = 2000
        };

        _pieceVerificationService.VerifyPieceFromStorage(
            Arg.Any<Torrent>(),
            Arg.Any<IList<TorrentFile>>(),
            Arg.Any<int>(),
            Arg.Any<byte[]>(),
            Arg.Any<IMultiFilePieceStorage>(),
            Arg.Any<string>()).Returns(true);

        _stateMachine.When(sm => sm.TransitionToChecking(Arg.Any<Torrent>()))
            .Do(callInfo => callInfo.Arg<Torrent>().Status = TorrentStatus.Checking);

        _torrentRepository.Get(24).Returns(persistedTorrent);

        var result = _service.Recheck(torrent);

        Assert.That(result.Status, Is.EqualTo(TorrentStatus.Paused));
        _stateMachine.DidNotReceive().TransitionFromChecking(Arg.Any<Torrent>());
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

    [Test]
    public void RecheckAsync_restores_prior_status_and_broadcasts_when_recheck_fails_after_checking()
    {
        var signalR = Substitute.For<IBroadcastSignalRMessage>();
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
            Id = 30,
            InfoHash = "hash-fail-recheck",
            Name = "Failing Recheck",
            Status = TorrentStatus.Downloading,
            PieceCount = 2,
            TotalSize = 2000
        };

        _torrentRepository.Get(30).Returns(torrent);
        _stateMachine.When(sm => sm.TransitionToChecking(Arg.Any<Torrent>()))
            .Do(ci => ci.Arg<Torrent>().Status = TorrentStatus.Checking);
        _pieceStorage
            .When(ps => ps.SetVerifiedPieces(Arg.Any<string>(), Arg.Any<bool[]>()))
            .Do(_ => throw new InvalidOperationException("simulated storage failure"));

        Assert.Throws<InvalidOperationException>(() => serviceWithSignalR.Recheck(torrent));

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentStatusChangedEvent>(e =>
            e.Torrent.Id == 30 && e.OldStatus == TorrentStatus.Checking && e.NewStatus == TorrentStatus.Downloading));
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentHashCheckCompletedEvent>(e => e.Torrent == torrent && !e.IsSuccessful));
        signalR.Received().BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "TorrentUpdated" && m.Action == NzbDrone.Core.Datastore.ModelAction.Updated));
        signalR.Received().BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TorrentRecheckProgress"));
    }

    [Test]
    public async System.Threading.Tasks.Task QueueRecheck_restores_status_and_broadcasts_when_recheck_fails_in_queue()
    {
        var signalR = Substitute.For<IBroadcastSignalRMessage>();
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
            Id = 31,
            InfoHash = "hash-queue-fail",
            Name = "Queued Failing Recheck",
            Status = TorrentStatus.Seeding,
            PieceCount = 1,
            PieceHashes = new byte[20],
            TotalSize = 1000
        };

        _torrentRepository.Get(31).Returns(torrent);
        _stateMachine.When(sm => sm.TransitionToChecking(Arg.Any<Torrent>()))
            .Do(ci => ci.Arg<Torrent>().Status = TorrentStatus.Checking);
        _pieceStorage
            .When(ps => ps.SetVerifiedPieces(Arg.Any<string>(), Arg.Any<bool[]>()))
            .Do(_ => throw new InvalidOperationException("simulated storage failure"));

        serviceWithSignalR.QueueRecheck(31);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline &&
                torrent.Status != TorrentStatus.Seeding &&
                torrent.Status != TorrentStatus.Paused)
        {
            await System.Threading.Tasks.Task.Delay(10);
        }

        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Seeding));
        signalR.Received().BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "TorrentUpdated" && m.Action == NzbDrone.Core.Datastore.ModelAction.Updated));
    }

    [Test]
    public async System.Threading.Tasks.Task RecheckAsync_uses_global_broadcast_only_for_recheck_signalr_events()
    {
        var signalR = Substitute.For<IBroadcastSignalRMessage>();
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
            Id = 32,
            Name = "No Torrent Group Fanout",
            Status = TorrentStatus.Downloading,
            PieceCount = 2,
            TotalSize = 2000
        };

        _torrentRepository.Get(32).Returns(torrent);

        await serviceWithSignalR.RecheckAsync(torrent);

        signalR.Received().BroadcastMessage(Arg.Is<SignalRMessage>(m => m.Name == "TorrentRecheckProgress"));
        signalR.DidNotReceive().BroadcastToTorrent(Arg.Any<int>(), Arg.Any<SignalRMessage>());
    }
}
