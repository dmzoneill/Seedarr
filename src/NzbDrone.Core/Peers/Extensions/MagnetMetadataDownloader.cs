using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers.Extensions;

public interface IMagnetMetadataDownloader
{
    int MaxPipelinedRequestsPerPeer { get; set; }
    TimeSpan RequestTimeout { get; set; }

    bool StartDownload(Torrent torrent, int metadataSize);
    void CancelDownload(string infoHash);
    bool IsDownloading(string infoHash);
    MetadataDownloadSession GetSession(string infoHash);

    bool RegisterPeer(Torrent torrent, PeerConnection peer, int? metadataSize = null);
    void UnregisterPeer(string infoHash, PeerConnection peer);
    void OnPeerHandshake(PeerConnection peer, Torrent torrent = null);
    void OnPeerDisconnected(PeerConnection peer, string infoHash = null);

    void HandleMetadataMessage(PeerConnection peer, MetadataMessage message, Torrent torrent = null);
    void HandleMetadataMessage(PeerConnection peer, byte[] rawPayload, Torrent torrent = null);
    void ScheduleRequests(string infoHash);
    void ProcessTimeouts(string infoHash = null);
}

public class PieceRequestState
{
    public int PieceIndex { get; set; }
    public PeerConnection Peer { get; set; }
    public DateTime RequestedAt { get; set; }
}

public class MetadataDownloadSession
{
    public object Lock { get; } = new();
    public Torrent Torrent { get; }
    public string InfoHash => Torrent.InfoHash;
    public int MetadataSize { get; }
    public int TotalPieces { get; }
    public byte[][] Pieces { get; }
    public HashSet<int> CompletedPieces { get; } = new();
    public Queue<int> UnassignedPieces { get; } = new();
    public Dictionary<int, PieceRequestState> InFlightRequests { get; } = new();
    public Dictionary<PeerConnection, HashSet<int>> PeerInFlightRequests { get; } = new();
    public Dictionary<int, HashSet<PeerConnection>> ExcludedPeersPerPiece { get; } = new();
    public List<PeerConnection> Peers { get; } = new();
    public bool IsComplete { get; set; }
    public bool IsFailed { get; set; }

    public MetadataDownloadSession(Torrent torrent, int metadataSize)
    {
        Torrent = torrent ?? throw new ArgumentNullException(nameof(torrent));
        MetadataSize = metadataSize;
        TotalPieces = (int)Math.Ceiling((double)metadataSize / MetadataExchange.MetadataBlockSize);
        Pieces = new byte[TotalPieces][];
        for (var i = 0; i < TotalPieces; i++)
        {
            UnassignedPieces.Enqueue(i);
        }
    }
}

public class MagnetMetadataDownloader : IMagnetMetadataDownloader,
    IHandle<PeerDisconnectedEvent>,
    IHandle<TorrentDeletedEvent>
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly IMetadataExchange _metadataExchange;
    private readonly IEventAggregator _eventAggregator;
    private readonly IConnectionManager _connectionManager;
    private readonly Logger _logger;

    private readonly ConcurrentDictionary<string, MetadataDownloadSession> _sessions =
        new(StringComparer.OrdinalIgnoreCase);

    public int MaxPipelinedRequestsPerPeer { get; set; } = 2;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public MagnetMetadataDownloader(
        ITorrentService torrentService = null,
        ITorrentFileService torrentFileService = null,
        IMetadataExchange metadataExchange = null,
        IEventAggregator eventAggregator = null,
        IConnectionManager connectionManager = null)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _metadataExchange = metadataExchange ?? new MetadataExchange();
        _eventAggregator = eventAggregator;
        _connectionManager = connectionManager;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool StartDownload(Torrent torrent, int metadataSize)
    {
        if (torrent == null || string.IsNullOrWhiteSpace(torrent.InfoHash))
        {
            _logger.Warn("Cannot start metadata download: torrent or infohash is missing");
            return false;
        }

        if (metadataSize <= 0 || metadataSize > MetadataExchange.MaxMetadataSize)
        {
            _logger.Warn(
                "Cannot start metadata download for {0}: invalid metadata_size {1} (max: {2})",
                torrent.InfoHash,
                metadataSize,
                MetadataExchange.MaxMetadataSize);
            return false;
        }

        var session = _sessions.GetOrAdd(torrent.InfoHash, _ => new MetadataDownloadSession(torrent, metadataSize));

        if (_connectionManager != null)
        {
            var connections = _connectionManager.GetConnections(torrent.InfoHash);
            lock (session.Lock)
            {
                foreach (var conn in connections)
                {
                    if (conn.IsConnected && conn.RemoteExtensions.ContainsKey("ut_metadata") && !session.Peers.Contains(conn))
                    {
                        session.Peers.Add(conn);
                    }
                }
            }
        }

        ScheduleRequests(session);
        return true;
    }

    public void CancelDownload(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        _sessions.TryRemove(infoHash, out _);
    }

    public bool IsDownloading(string infoHash)
    {
        return !string.IsNullOrWhiteSpace(infoHash) &&
            _sessions.TryGetValue(infoHash, out var session) &&
            !session.IsComplete && !session.IsFailed;
    }

    public MetadataDownloadSession GetSession(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        _sessions.TryGetValue(infoHash, out var session);
        return session;
    }

    public bool RegisterPeer(Torrent torrent, PeerConnection peer, int? metadataSize = null)
    {
        if (peer == null)
        {
            return false;
        }

        var infoHash = torrent?.InfoHash ?? peer.InfoHash;
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return false;
        }

        var effectiveSize = metadataSize ?? peer.MetadataSize;

        if (!_sessions.TryGetValue(infoHash, out var session))
        {
            if (!effectiveSize.HasValue || effectiveSize.Value <= 0 || effectiveSize.Value > MetadataExchange.MaxMetadataSize)
            {
                return false;
            }

            var actualTorrent = torrent ?? peer.MatchedTorrent ?? ResolveTorrent(infoHash);
            if (actualTorrent == null)
            {
                actualTorrent = new Torrent
                {
                    InfoHash = infoHash,
                    Name = infoHash,
                    Status = TorrentStatus.Downloading
                };
            }

            StartDownload(actualTorrent, effectiveSize.Value);
            _sessions.TryGetValue(infoHash, out session);
        }

        if (session != null)
        {
            lock (session.Lock)
            {
                if (!session.Peers.Contains(peer))
                {
                    session.Peers.Add(peer);
                }

                ScheduleRequests(session);
            }

            return true;
        }

        return false;
    }

    public void UnregisterPeer(string infoHash, PeerConnection peer)
    {
        if (peer == null)
        {
            return;
        }

        var session = GetSession(infoHash);
        if (session != null)
        {
            lock (session.Lock)
            {
                session.Peers.Remove(peer);
                if (session.PeerInFlightRequests.Remove(peer, out var inFlight))
                {
                    foreach (var piece in inFlight)
                    {
                        session.InFlightRequests.Remove(piece);
                        if (!session.CompletedPieces.Contains(piece) && !session.UnassignedPieces.Contains(piece))
                        {
                            session.UnassignedPieces.Enqueue(piece);
                        }
                    }
                }

                ScheduleRequests(session);
            }
        }
    }

    public void OnPeerHandshake(PeerConnection peer, Torrent torrent = null)
    {
        if (peer == null || !peer.RemoteExtensions.ContainsKey("ut_metadata"))
        {
            return;
        }

        var infoHash = torrent?.InfoHash ?? peer.InfoHash;
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        var actualTorrent = torrent ?? peer.MatchedTorrent ?? ResolveTorrent(infoHash);
        if (actualTorrent == null || actualTorrent.PieceCount > 0)
        {
            return;
        }

        RegisterPeer(actualTorrent, peer, peer.MetadataSize);
    }

    public void OnPeerDisconnected(PeerConnection peer, string infoHash = null)
    {
        if (peer == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            UnregisterPeer(infoHash, peer);
            return;
        }

        foreach (var session in _sessions.Values)
        {
            lock (session.Lock)
            {
                if (session.Peers.Contains(peer))
                {
                    UnregisterPeer(session.InfoHash, peer);
                }
            }
        }
    }

    public void ScheduleRequests(string infoHash)
    {
        var session = GetSession(infoHash);
        if (session != null)
        {
            ScheduleRequests(session);
        }
    }

    private void ScheduleRequests(MetadataDownloadSession session)
    {
        lock (session.Lock)
        {
            if (session.IsComplete || session.IsFailed)
            {
                return;
            }

            var eligiblePeers = session.Peers
                .Where(p => p.IsConnected && p.RemoteExtensions.ContainsKey("ut_metadata"))
                .ToList();

            if (eligiblePeers.Count == 0)
            {
                return;
            }

            var unassignedCount = session.UnassignedPieces.Count;
            for (var i = 0; i < unassignedCount; i++)
            {
                var pieceIndex = session.UnassignedPieces.Dequeue();
                var candidatePeers = eligiblePeers
                    .Where(p => (!session.ExcludedPeersPerPiece.TryGetValue(pieceIndex, out var excluded) || !excluded.Contains(p)))
                    .Where(p => GetPeerInFlightCount(session, p) < MaxPipelinedRequestsPerPeer)
                    .OrderBy(p => GetPeerInFlightCount(session, p))
                    .ToList();

                if (candidatePeers.Count == 0 && eligiblePeers.All(p => session.ExcludedPeersPerPiece.TryGetValue(pieceIndex, out var excluded) && excluded.Contains(p)))
                {
                    candidatePeers = eligiblePeers
                        .Where(p => GetPeerInFlightCount(session, p) < MaxPipelinedRequestsPerPeer)
                        .OrderBy(p => GetPeerInFlightCount(session, p))
                        .ToList();
                }

                if (candidatePeers.Count > 0)
                {
                    var bestPeer = candidatePeers[0];
                    SendPieceRequest(session, bestPeer, pieceIndex);
                }
                else
                {
                    session.UnassignedPieces.Enqueue(pieceIndex);
                }
            }
        }
    }

    private static int GetPeerInFlightCount(MetadataDownloadSession session, PeerConnection peer)
    {
        return session.PeerInFlightRequests.TryGetValue(peer, out var inFlight) ? inFlight.Count : 0;
    }

    private void SendPieceRequest(MetadataDownloadSession session, PeerConnection peer, int pieceIndex)
    {
        if (!peer.RemoteExtensions.TryGetValue("ut_metadata", out var extId))
        {
            session.UnassignedPieces.Enqueue(pieceIndex);
            return;
        }

        var requestBytes = _metadataExchange.BuildMetadataRequest(pieceIndex);
        var payload = new byte[1 + requestBytes.Length];
        payload[0] = (byte)extId;
        Array.Copy(requestBytes, 0, payload, 1, requestBytes.Length);

        session.InFlightRequests[pieceIndex] = new PieceRequestState
        {
            PieceIndex = pieceIndex,
            Peer = peer,
            RequestedAt = DateTime.UtcNow
        };

        if (!session.PeerInFlightRequests.TryGetValue(peer, out var inFlight))
        {
            inFlight = new HashSet<int>();
            session.PeerInFlightRequests[peer] = inFlight;
        }

        inFlight.Add(pieceIndex);

        try
        {
            peer.SendMessage(new PeerMessage
            {
                Type = PeerMessageType.Extended,
                Payload = payload
            });
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to send ut_metadata request for piece {0} to peer {1}", pieceIndex, peer.RemoteIp);
            inFlight.Remove(pieceIndex);
            session.InFlightRequests.Remove(pieceIndex);
            session.UnassignedPieces.Enqueue(pieceIndex);
        }
    }

    public void HandleMetadataMessage(PeerConnection peer, byte[] rawPayload, Torrent torrent = null)
    {
        var metaMsg = _metadataExchange.ParseMetadataMessage(rawPayload);
        HandleMetadataMessage(peer, metaMsg, torrent);
    }

    public void HandleMetadataMessage(PeerConnection peer, MetadataMessage message, Torrent torrent = null)
    {
        if (peer == null || message == null)
        {
            return;
        }

        var infoHash = torrent?.InfoHash ?? peer.InfoHash;
        MetadataDownloadSession session = null;
        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            session = GetSession(infoHash);
        }

        if (session == null)
        {
            session = _sessions.Values.FirstOrDefault(s => s.Peers.Contains(peer));
        }

        if (session == null)
        {
            return;
        }

        lock (session.Lock)
        {
            if (session.IsComplete || session.IsFailed)
            {
                return;
            }

            if (message.MessageType == 2)
            {
                _logger.Debug("Peer {0} rejected metadata piece {1}", peer.RemoteIp, message.Piece);
                if (!session.ExcludedPeersPerPiece.TryGetValue(message.Piece, out var excluded))
                {
                    excluded = new HashSet<PeerConnection>();
                    session.ExcludedPeersPerPiece[message.Piece] = excluded;
                }

                excluded.Add(peer);

                if (session.InFlightRequests.TryGetValue(message.Piece, out var reqState) && reqState.Peer == peer)
                {
                    session.InFlightRequests.Remove(message.Piece);
                    if (session.PeerInFlightRequests.TryGetValue(peer, out var inFlight))
                    {
                        inFlight.Remove(message.Piece);
                    }

                    if (!session.CompletedPieces.Contains(message.Piece) && !session.UnassignedPieces.Contains(message.Piece))
                    {
                        session.UnassignedPieces.Enqueue(message.Piece);
                    }

                    ScheduleRequests(session);
                }

                return;
            }

            if (message.MessageType == 1)
            {
                if (session.InFlightRequests.TryGetValue(message.Piece, out var reqState))
                {
                    session.InFlightRequests.Remove(message.Piece);
                    if (session.PeerInFlightRequests.TryGetValue(reqState.Peer, out var inFlight))
                    {
                        inFlight.Remove(message.Piece);
                    }
                }
                else if (session.PeerInFlightRequests.TryGetValue(peer, out var inFlight))
                {
                    inFlight.Remove(message.Piece);
                }

                if (message.Piece < 0 || message.Piece >= session.TotalPieces || message.Data == null)
                {
                    _logger.Warn("Invalid metadata piece index or data received from {0}", peer.RemoteIp);
                    if (!session.CompletedPieces.Contains(message.Piece) && !session.UnassignedPieces.Contains(message.Piece))
                    {
                        session.UnassignedPieces.Enqueue(message.Piece);
                    }

                    ScheduleRequests(session);
                    return;
                }

                var expectedSize = (message.Piece == session.TotalPieces - 1)
                    ? session.MetadataSize - (message.Piece * MetadataExchange.MetadataBlockSize)
                    : MetadataExchange.MetadataBlockSize;

                if (message.Data.Length != expectedSize)
                {
                    _logger.Warn("Metadata piece {0} length {1} did not match expected {2}", message.Piece, message.Data.Length, expectedSize);
                    if (!session.CompletedPieces.Contains(message.Piece) && !session.UnassignedPieces.Contains(message.Piece))
                    {
                        session.UnassignedPieces.Enqueue(message.Piece);
                    }

                    ScheduleRequests(session);
                    return;
                }

                session.Pieces[message.Piece] = message.Data;
                session.CompletedPieces.Add(message.Piece);

                if (session.CompletedPieces.Count == session.TotalPieces)
                {
                    var assembled = _metadataExchange.ReassembleMetadata(session.Pieces, session.MetadataSize, session.Torrent.InfoHash, session.Torrent.Name);
                    if (assembled == null)
                    {
                        _logger.Error("Corrupted metadata detected for {0}. Discarding.", session.Torrent.InfoHash);
                        session.IsFailed = true;
                        Array.Clear(session.Pieces, 0, session.Pieces.Length);
                        session.CompletedPieces.Clear();
                        return;
                    }

                    IngestMetadata(session, assembled);
                }
                else
                {
                    ScheduleRequests(session);
                }
            }
        }
    }

    public void ProcessTimeouts(string infoHash = null)
    {
        IEnumerable<MetadataDownloadSession> sessionsToProcess;
        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            var session = GetSession(infoHash);
            sessionsToProcess = session != null ? new[] { session } : Enumerable.Empty<MetadataDownloadSession>();
        }
        else
        {
            sessionsToProcess = _sessions.Values.ToList();
        }

        var now = DateTime.UtcNow;

        foreach (var session in sessionsToProcess)
        {
            lock (session.Lock)
            {
                if (session.IsComplete || session.IsFailed)
                {
                    continue;
                }

                var timedOutPieces = new List<int>();
                foreach (var (pieceIndex, reqState) in session.InFlightRequests)
                {
                    if (now - reqState.RequestedAt >= RequestTimeout)
                    {
                        timedOutPieces.Add(pieceIndex);
                    }
                }

                foreach (var pieceIndex in timedOutPieces)
                {
                    if (session.InFlightRequests.Remove(pieceIndex, out var reqState))
                    {
                        _logger.Warn("Metadata request for piece {0} timed out on peer {1}", pieceIndex, reqState.Peer?.RemoteIp);
                        if (reqState.Peer != null)
                        {
                            if (!session.ExcludedPeersPerPiece.TryGetValue(pieceIndex, out var excluded))
                            {
                                excluded = new HashSet<PeerConnection>();
                                session.ExcludedPeersPerPiece[pieceIndex] = excluded;
                            }

                            excluded.Add(reqState.Peer);

                            if (session.PeerInFlightRequests.TryGetValue(reqState.Peer, out var inFlight))
                            {
                                inFlight.Remove(pieceIndex);
                            }
                        }

                        if (!session.CompletedPieces.Contains(pieceIndex) && !session.UnassignedPieces.Contains(pieceIndex))
                        {
                            session.UnassignedPieces.Enqueue(pieceIndex);
                        }
                    }
                }

                if (timedOutPieces.Count > 0)
                {
                    ScheduleRequests(session);
                }
            }
        }
    }

    public void Handle(PeerDisconnectedEvent message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.InfoHash))
        {
            return;
        }

        var session = GetSession(message.InfoHash);
        if (session != null)
        {
            lock (session.Lock)
            {
                var peer = session.Peers.FirstOrDefault(p => string.Equals(p.RemoteIp, message.RemoteIp, StringComparison.OrdinalIgnoreCase));
                if (peer != null)
                {
                    UnregisterPeer(message.InfoHash, peer);
                }
            }
        }
    }

    public void Handle(TorrentDeletedEvent message)
    {
        var infoHash = message?.Torrent?.InfoHash;
        if (!string.IsNullOrWhiteSpace(infoHash))
        {
            CancelDownload(infoHash);
        }
    }

    private bool IngestMetadata(MetadataDownloadSession session, byte[] assembledBytes)
    {
        try
        {
            var parser = new BencodeParser();
            using var stream = new MemoryStream(assembledBytes);
            var info = parser.Parse<BDictionary>(stream);

            var name = GetStringWithUtf8Fallback(info, "name") ?? session.Torrent.Name;

            var pieceLength = info.TryGetValue("piece length", out var plObj) && plObj is BNumber plNum
                ? (int)plNum.Value
                : session.Torrent.PieceLength;

            var pieceCount = info.TryGetValue("pieces", out var piecesObj) && piecesObj is BString piecesStr
                ? piecesStr.Value.Length / 20
                : session.Torrent.PieceCount;

            var files = new List<TorrentFile>();
            long totalSize;

            if (info.TryGetValue("files", out var filesObj) && filesObj is BList filesList)
            {
                var rootDirName = name?.Replace('\\', '/').Trim('/', '\\');

                foreach (var fileObj in filesList)
                {
                    if (fileObj is not BDictionary fileDict)
                    {
                        continue;
                    }

                    var fileLength = fileDict.TryGetValue("length", out var flObj) && flObj is BNumber flNum
                        ? flNum.Value
                        : 0;

                    var pathList = GetPathListWithUtf8Fallback(fileDict);
                    var pathParts = pathList?.OfType<BString>()
                        .Select(p => DecodeBString(p).Replace('\\', '/').Trim('/', '\\'))
                        .Where(p => !string.IsNullOrWhiteSpace(p));

                    var relativePath = pathParts != null ? string.Join("/", pathParts) : string.Empty;
                    var fullRelativePath = string.IsNullOrEmpty(rootDirName)
                        ? relativePath
                        : string.IsNullOrEmpty(relativePath)
                            ? rootDirName
                            : $"{rootDirName}/{relativePath}";

                    var isPadding = IsPadding(fileDict, fullRelativePath);

                    files.Add(new TorrentFile
                    {
                        TorrentId = session.Torrent.Id,
                        Path = fullRelativePath,
                        Size = fileLength,
                        IsPaddingFile = isPadding
                    });
                }

                totalSize = files.Sum(f => f.Size);
            }
            else
            {
                var length = info.TryGetValue("length", out var lenObj) && lenObj is BNumber lenNum
                    ? lenNum.Value
                    : (session.Torrent.TotalSize > 0 ? session.Torrent.TotalSize : 0);

                totalSize = length;
                files.Add(new TorrentFile
                {
                    TorrentId = session.Torrent.Id,
                    Path = name,
                    Size = totalSize,
                    IsPaddingFile = false
                });
            }

            session.Torrent.Name = name;
            session.Torrent.PieceLength = pieceLength;
            session.Torrent.PieceCount = pieceCount;
            session.Torrent.TotalSize = totalSize;
            session.Torrent.Files = files;

            if (session.Torrent.Status != TorrentStatus.Seeding)
            {
                session.Torrent.Status = TorrentStatus.Downloading;
            }

            if (_torrentFileService != null)
            {
                try
                {
                    if (session.Torrent.Id > 0)
                    {
                        _torrentFileService.DeleteByTorrentId(session.Torrent.Id);
                    }

                    foreach (var f in files)
                    {
                        _torrentFileService.Add(f);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to persist torrent files for {0}", session.Torrent.Name);
                }
            }

            if (_torrentService != null)
            {
                try
                {
                    _torrentService.Update(session.Torrent);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to update torrent record for {0}", session.Torrent.Name);
                }
            }

            _eventAggregator?.PublishEvent(new TorrentMetadataResolvedEvent(session.Torrent));
            session.IsComplete = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse and ingest metadata for torrent {0}", session.Torrent.InfoHash);
            session.IsFailed = true;
            return false;
        }
    }

    private static string DecodeBString(BString bString)
    {
        if (bString == null)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(bString.Value.Span);
        }
        catch
        {
            return Encoding.Latin1.GetString(bString.Value.Span);
        }
    }

    private static string GetStringWithUtf8Fallback(BDictionary dict, string primaryKey)
    {
        var utf8Key1 = primaryKey + ".utf-8";
        var utf8Key2 = primaryKey + ".utf8";

        if (dict.TryGetValue(utf8Key1, out var val1) && val1 is BString bs1)
        {
            return DecodeBString(bs1);
        }

        if (dict.TryGetValue(utf8Key2, out var val2) && val2 is BString bs2)
        {
            return DecodeBString(bs2);
        }

        if (dict.TryGetValue(primaryKey, out var val) && val is BString bs)
        {
            return DecodeBString(bs);
        }

        return null;
    }

    private static BList GetPathListWithUtf8Fallback(BDictionary dict)
    {
        if (dict.TryGetValue("path.utf-8", out var val1))
        {
            if (val1 is BList bl1)
            {
                return bl1;
            }

            if (val1 is BString bs1)
            {
                return new BList { bs1 };
            }
        }

        if (dict.TryGetValue("path.utf8", out var val2))
        {
            if (val2 is BList bl2)
            {
                return bl2;
            }

            if (val2 is BString bs2)
            {
                return new BList { bs2 };
            }
        }

        if (dict.TryGetValue("path", out var val))
        {
            if (val is BList bl)
            {
                return bl;
            }

            if (val is BString bs)
            {
                return new BList { bs };
            }
        }

        return null;
    }

    private static bool IsPadding(BDictionary fileDict, string path)
    {
        if (fileDict.TryGetValue("attr", out var attrVal) && attrVal is BString attrStr && attrStr.ToString().Contains('p'))
        {
            return true;
        }

        var normalizedPath = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedPath);
        return fileName.StartsWith("_____padding_file_", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(".pad/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/.pad/", StringComparison.OrdinalIgnoreCase);
    }

    private Torrent ResolveTorrent(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash) || _torrentService == null)
        {
            return null;
        }

        try
        {
            return _torrentService.FindByInfoHash(infoHash) ?? _torrentService.GetByInfoHash(infoHash);
        }
        catch
        {
            return null;
        }
    }
}
