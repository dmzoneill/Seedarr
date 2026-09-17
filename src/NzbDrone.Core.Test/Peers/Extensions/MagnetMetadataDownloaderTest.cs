using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BencodeNET.Objects;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Peers;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Peers.Extensions;

[TestFixture]
public class MagnetMetadataDownloaderTest
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private IMetadataExchange _metadataExchange;
    private IEventAggregator _eventAggregator;
    private MagnetMetadataDownloader _downloader;

    [SetUp]
    public void Setup()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _metadataExchange = new MetadataExchange();
        _eventAggregator = Substitute.For<IEventAggregator>();

        _downloader = new MagnetMetadataDownloader(
            _torrentService,
            _torrentFileService,
            _metadataExchange,
            _eventAggregator);
    }

    [Test]
    public void MultiPeer_piece_distribution_and_reassembly_should_succeed()
    {
        var rawMetadata = CreateSampleMetadata(out var expectedInfoHash, totalSize: 35000);
        var torrent = new Torrent
        {
            Id = 42,
            Name = "sample_torrent",
            InfoHash = expectedInfoHash,
            PieceCount = 0,
            TotalSize = 0,
            Status = TorrentStatus.Queued
        };

        var peer1 = CreateMockPeer("192.168.1.10", 6881);
        var peer2 = CreateMockPeer("192.168.1.20", 6881);

        _downloader.StartDownload(torrent, rawMetadata.Length);
        _downloader.RegisterPeer(torrent, peer1);
        _downloader.RegisterPeer(torrent, peer2);

        var session = _downloader.GetSession(expectedInfoHash);
        Assert.That(session, Is.Not.Null);
        Assert.That(session.TotalPieces, Is.EqualTo(3));

        // Pieces should be distributed between peer1 and peer2
        Assert.That(session.InFlightRequests.Values.Any(r => r.Peer == peer1), Is.True);
        Assert.That(session.InFlightRequests.Values.Any(r => r.Peer == peer2), Is.True);

        // Deliver chunks for all 3 pieces
        DeliverChunk(peer1, torrent, rawMetadata, 0);
        DeliverChunk(peer2, torrent, rawMetadata, 1);
        DeliverChunk(peer1, torrent, rawMetadata, 2);

        Assert.That(session.IsComplete, Is.True);
        Assert.That(session.CompletedPieces.Count, Is.EqualTo(3));
        Assert.That(torrent.Name, Is.EqualTo("sample_torrent"));
        Assert.That(torrent.PieceCount, Is.GreaterThan(0));
        Assert.That(torrent.TotalSize, Is.EqualTo(35000));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));
    }

    [Test]
    public void Peer_rejection_should_requeue_piece_to_alternative_peer()
    {
        var rawMetadata = CreateSampleMetadata(out var expectedInfoHash, totalSize: 20000);
        var torrent = new Torrent
        {
            Id = 1,
            Name = "rejection_test",
            InfoHash = expectedInfoHash,
            PieceCount = 0,
            TotalSize = 0
        };

        var peer1 = CreateMockPeer("10.0.0.1", 6881);
        var peer2 = CreateMockPeer("10.0.0.2", 6881);

        _downloader.MaxPipelinedRequestsPerPeer = 1;
        _downloader.StartDownload(torrent, rawMetadata.Length);
        _downloader.RegisterPeer(torrent, peer1);
        _downloader.RegisterPeer(torrent, peer2);

        var session = _downloader.GetSession(expectedInfoHash);
        Assert.That(session.TotalPieces, Is.EqualTo(2));

        // Find which piece was assigned to peer1
        var peer1Piece = session.InFlightRequests.First(r => r.Value.Peer == peer1).Key;

        // Peer1 sends reject message for its piece
        var rejectMessage = new MetadataMessage
        {
            MessageType = 2,
            Piece = peer1Piece
        };

        _downloader.HandleMetadataMessage(peer1, rejectMessage, torrent);

        // Deliver the other piece from peer2
        var peer2Piece = 1 - peer1Piece;
        DeliverChunk(peer2, torrent, rawMetadata, peer2Piece);

        // Peer2 is now free; the rejected piece should be re-scheduled to peer2
        Assert.That(session.InFlightRequests.ContainsKey(peer1Piece), Is.True);
        Assert.That(session.InFlightRequests[peer1Piece].Peer, Is.EqualTo(peer2));

        // Deliver the re-queued piece from peer2
        DeliverChunk(peer2, torrent, rawMetadata, peer1Piece);

        Assert.That(session.IsComplete, Is.True);
    }

    [Test]
    public void Peer_timeout_should_requeue_piece_to_alternative_peer()
    {
        var rawMetadata = CreateSampleMetadata(out var expectedInfoHash, totalSize: 20000);
        var torrent = new Torrent
        {
            Id = 2,
            Name = "timeout_test",
            InfoHash = expectedInfoHash,
            PieceCount = 0,
            TotalSize = 0
        };

        var peer1 = CreateMockPeer("10.0.0.1", 6881);
        var peer2 = CreateMockPeer("10.0.0.2", 6881);

        _downloader.MaxPipelinedRequestsPerPeer = 1;
        _downloader.RequestTimeout = TimeSpan.FromMilliseconds(50);
        _downloader.StartDownload(torrent, rawMetadata.Length);
        _downloader.RegisterPeer(torrent, peer1);
        _downloader.RegisterPeer(torrent, peer2);

        var session = _downloader.GetSession(expectedInfoHash);
        var peer1Piece = session.InFlightRequests.First(r => r.Value.Peer == peer1).Key;

        // Deliver peer2's piece so peer2 becomes free
        var peer2Piece = 1 - peer1Piece;
        DeliverChunk(peer2, torrent, rawMetadata, peer2Piece);

        // Manually adjust request timestamp to simulate timeout
        session.InFlightRequests[peer1Piece].RequestedAt = DateTime.UtcNow.AddSeconds(-20);

        _downloader.ProcessTimeouts(expectedInfoHash);

        // Piece should now be assigned to peer2
        Assert.That(session.InFlightRequests.ContainsKey(peer1Piece), Is.True);
        Assert.That(session.InFlightRequests[peer1Piece].Peer, Is.EqualTo(peer2));

        DeliverChunk(peer2, torrent, rawMetadata, peer1Piece);
        Assert.That(session.IsComplete, Is.True);
    }

    [Test]
    public void Peer_disconnection_should_requeue_pending_pieces()
    {
        var rawMetadata = CreateSampleMetadata(out var expectedInfoHash, totalSize: 20000);
        var torrent = new Torrent
        {
            Id = 3,
            Name = "disconnect_test",
            InfoHash = expectedInfoHash
        };

        var peer1 = CreateMockPeer("10.0.0.1", 6881);
        var peer2 = CreateMockPeer("10.0.0.2", 6881);

        _downloader.MaxPipelinedRequestsPerPeer = 1;
        _downloader.StartDownload(torrent, rawMetadata.Length);
        _downloader.RegisterPeer(torrent, peer1);
        _downloader.RegisterPeer(torrent, peer2);

        var session = _downloader.GetSession(expectedInfoHash);
        var peer1Piece = session.InFlightRequests.First(r => r.Value.Peer == peer1).Key;
        var peer2Piece = 1 - peer1Piece;

        DeliverChunk(peer2, torrent, rawMetadata, peer2Piece);

        // Disconnect peer1
        _downloader.OnPeerDisconnected(peer1, expectedInfoHash);

        // Piece should be scheduled to peer2
        Assert.That(session.InFlightRequests.ContainsKey(peer1Piece), Is.True);
        Assert.That(session.InFlightRequests[peer1Piece].Peer, Is.EqualTo(peer2));

        DeliverChunk(peer2, torrent, rawMetadata, peer1Piece);
        Assert.That(session.IsComplete, Is.True);
    }

    [Test]
    public void Corrupted_metadata_should_be_rejected_and_discarded()
    {
        CreateSampleMetadata(out var expectedInfoHash, totalSize: 20000);
        var torrent = new Torrent
        {
            Id = 4,
            Name = "poisoned_torrent",
            InfoHash = expectedInfoHash,
            PieceCount = 0,
            TotalSize = 0,
            Status = TorrentStatus.Queued
        };

        var peer1 = CreateMockPeer("10.0.0.1", 6881);
        const int metadataSize = 20000;

        _downloader.StartDownload(torrent, metadataSize);
        _downloader.RegisterPeer(torrent, peer1);

        var session = _downloader.GetSession(expectedInfoHash);
        Assert.That(session.TotalPieces, Is.EqualTo(2));

        // Deliver corrupted data chunks (random bytes that won't match SHA-1)
        var badChunk0 = new byte[MetadataExchange.MetadataBlockSize];
        new Random(42).NextBytes(badChunk0);
        var badChunk1 = new byte[metadataSize - MetadataExchange.MetadataBlockSize];
        new Random(43).NextBytes(badChunk1);

        var chunk0Message = new MetadataMessage
        {
            MessageType = 1,
            Piece = 0,
            TotalSize = metadataSize,
            Data = badChunk0
        };

        _downloader.HandleMetadataMessage(peer1, chunk0Message, torrent);

        var chunk1Message = new MetadataMessage
        {
            MessageType = 1,
            Piece = 1,
            TotalSize = metadataSize,
            Data = badChunk1
        };

        _downloader.HandleMetadataMessage(peer1, chunk1Message, torrent);

        // Corrupted metadata should cause the session to fail and discard the pieces
        Assert.That(session.IsComplete, Is.False);
        Assert.That(session.IsFailed, Is.True);
        Assert.That(session.CompletedPieces.Count, Is.EqualTo(0));
        Assert.That(session.Pieces.All(p => p == null), Is.True);

        // Torrent properties must not be modified and events must not be fired
        Assert.That(torrent.PieceCount, Is.EqualTo(0));
        _torrentService.DidNotReceive().Update(torrent);
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<TorrentMetadataResolvedEvent>());
    }

    [Test]
    public void Successful_metadata_resolution_should_update_torrent_properties_and_fire_completion()
    {
        var rawMetadata = CreateMultiFileSampleMetadata(out var expectedInfoHash);
        var torrent = new Torrent
        {
            Id = 99,
            Name = "multifile_torrent",
            InfoHash = expectedInfoHash,
            PieceCount = 0,
            TotalSize = 0,
            Status = TorrentStatus.Queued
        };

        var peer = CreateMockPeer("192.168.1.50", 6881);

        _downloader.StartDownload(torrent, rawMetadata.Length);
        _downloader.RegisterPeer(torrent, peer);

        var session = _downloader.GetSession(expectedInfoHash);
        for (var i = 0; i < session.TotalPieces; i++)
        {
            DeliverChunk(peer, torrent, rawMetadata, i);
        }

        Assert.That(session.IsComplete, Is.True);
        Assert.That(torrent.Name, Is.EqualTo("multifile_torrent"));
        Assert.That(torrent.PieceLength, Is.EqualTo(16384));
        Assert.That(torrent.PieceCount, Is.EqualTo(3));
        Assert.That(torrent.TotalSize, Is.EqualTo(40000));
        Assert.That(torrent.Status, Is.EqualTo(TorrentStatus.Downloading));

        // Verify files saved
        _torrentFileService.Received().DeleteByTorrentId(99);
        _torrentFileService.Received(2).Add(Arg.Any<TorrentFile>());

        // Verify torrent updated in service
        _torrentService.Received().Update(torrent);

        // Verify event published
        _eventAggregator.Received().PublishEvent(Arg.Is<TorrentMetadataResolvedEvent>(e => e.Torrent == torrent));
    }

    [Test]
    public void StartDownload_should_reject_metadata_size_exceeding_10_MiB()
    {
        var torrent = new Torrent
        {
            Id = 5,
            InfoHash = "0123456789abcdef0123456789abcdef01234567"
        };

        var result = _downloader.StartDownload(torrent, MetadataExchange.MaxMetadataSize + 1);

        Assert.That(result, Is.False);
        Assert.That(_downloader.GetSession(torrent.InfoHash), Is.Null);
    }

    private static PeerConnection CreateMockPeer(string ip, int port)
    {
        var peer = new PeerConnection(new MemoryStream(), ip, port);
        peer.RemoteExtensions["ut_metadata"] = 1;
        return peer;
    }

    private void DeliverChunk(PeerConnection peer, Torrent torrent, byte[] rawMetadata, int pieceIndex)
    {
        var offset = pieceIndex * MetadataExchange.MetadataBlockSize;
        var chunkSize = Math.Min(MetadataExchange.MetadataBlockSize, rawMetadata.Length - offset);
        var chunk = new byte[chunkSize];
        Array.Copy(rawMetadata, offset, chunk, 0, chunkSize);

        var message = new MetadataMessage
        {
            MessageType = 1,
            Piece = pieceIndex,
            TotalSize = rawMetadata.Length,
            Data = chunk
        };

        _downloader.HandleMetadataMessage(peer, message, torrent);
    }

    private static byte[] CreateSampleMetadata(out string infoHash, long totalSize = 35000)
    {
        var piecesCount = (int)Math.Ceiling((double)totalSize / 16384);
        var piecesBytes = new byte[piecesCount * 20];
        new Random(12345).NextBytes(piecesBytes);

        var dict = new BDictionary
        {
            ["name"] = new BString("sample_torrent"),
            ["piece length"] = new BNumber(16384),
            ["pieces"] = new BString(piecesBytes),
            ["length"] = new BNumber(totalSize)
        };

        var raw = dict.EncodeAsBytes();
        if (raw.Length < totalSize)
        {
            var padNeeded = (int)totalSize - raw.Length;
            dict["padding"] = new BString(new byte[padNeeded]);
            raw = dict.EncodeAsBytes();
            while (raw.Length < totalSize)
            {
                padNeeded += (int)totalSize - raw.Length;
                dict["padding"] = new BString(new byte[padNeeded]);
                raw = dict.EncodeAsBytes();
            }

            while (raw.Length > totalSize)
            {
                padNeeded -= raw.Length - (int)totalSize;
                dict["padding"] = new BString(new byte[padNeeded]);
                raw = dict.EncodeAsBytes();
            }
        }

        infoHash = Convert.ToHexString(SHA1.HashData(raw));
        return raw;
    }

    private static byte[] CreateMultiFileSampleMetadata(out string infoHash)
    {
        var piecesBytes = new byte[3 * 20];
        new Random(54321).NextBytes(piecesBytes);

        var file1 = new BDictionary
        {
            ["length"] = new BNumber(25000),
            ["path"] = new BList { new BString("video"), new BString("movie.mp4") }
        };

        var file2 = new BDictionary
        {
            ["length"] = new BNumber(15000),
            ["path"] = new BList { new BString("sample.nfo") }
        };

        var dict = new BDictionary
        {
            ["name"] = new BString("multifile_torrent"),
            ["piece length"] = new BNumber(16384),
            ["pieces"] = new BString(piecesBytes),
            ["files"] = new BList { file1, file2 }
        };

        var raw = dict.EncodeAsBytes();
        infoHash = Convert.ToHexString(SHA1.HashData(raw));
        return raw;
    }
}
