using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers.Encryption;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public class PeerServer : BackgroundService
{
    private readonly IConfigService _configService;
    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;

    public PeerServer(IConfigService configService, ITorrentService torrentService)
    {
        _configService = configService;
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    private EncryptionMode GetEncryptionMode()
    {
        return _configService.EncryptionMode switch
        {
            "required" => EncryptionMode.RequireEncrypted,
            "disabled" => EncryptionMode.PreferPlainText,
            _ => EncryptionMode.PreferEncrypted
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listenerTask = RunListenerAsync(stoppingToken);
        var contactTask = RunPeerContactLoopAsync(stoppingToken);
        await Task.WhenAll(listenerTask, contactTask);
    }

    private async Task RunListenerAsync(CancellationToken stoppingToken)
    {
        var listeningPort = _configService.ListeningPort;
        var listener = new TcpListener(IPAddress.Any, listeningPort);

        try
        {
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                _logger.Warn(ex, "Peer server failed to bind port {0}, skipping", listeningPort);
                return;
            }

            _logger.Info("Peer server listening on port {0}", listeningPort);

            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(() => HandleConnection(client), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task RunPeerContactLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var intervalSeconds = _configService.PeerContactIntervalSeconds;
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);

                var torrents = _torrentService.GetAll()
                    .Where(t => t.Status == TorrentStatus.Seeding || t.Status == TorrentStatus.Downloading)
                    .ToList();

                _logger.Debug(
                    "Peer contact cycle: {0} active torrents (interval: {1}s)",
                    torrents.Count,
                    intervalSeconds);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
    }

    private void HandleConnection(TcpClient client)
    {
        using var connection = new PeerConnection(client);
        connection.HandshakeTimeoutMs = _configService.HandshakeTimeoutSeconds * 1000;
        connection.MessageReadTimeoutMs = _configService.MessageReadTimeoutSeconds * 1000;
        connection.KeepAliveIntervalSeconds = _configService.KeepAliveIntervalSeconds;
        connection.MaxPipelinedRequests = _configService.PeerRequestCount;
        connection.IdleChance = _configService.PeerIdleChance;

        _logger.Debug("Incoming peer: {0}:{1}", connection.RemoteIp, connection.RemotePort);

        try
        {
            // Attempt MSE/PE negotiation - this will detect plain BT handshakes and fall through
            var negotiated = connection.NegotiateEncryptionIncoming(ValidateInfoHash, GetEncryptionMode());
            if (!negotiated)
            {
                _logger.Debug("Encryption negotiation failed from {0}", connection.RemoteIp);
                return;
            }

            if (!connection.ReceiveHandshake())
            {
                _logger.Debug("Invalid handshake from {0}", connection.RemoteIp);
                return;
            }

            // Find matching torrent
            var torrents = _torrentService.GetAll();
            var torrent = torrents.Find(t => string.Equals(t.InfoHash, connection.InfoHash, StringComparison.OrdinalIgnoreCase));

            if (torrent == null)
            {
                _logger.Debug("Unknown info hash from {0}: {1}", connection.RemoteIp, connection.InfoHash);
                return;
            }

            // Send our handshake back
            var peerId = "-SD1000-000000000000";
            connection.SendHandshake(torrent.InfoHash, peerId);

            _logger.Debug(
                "Peer {0} connected (encrypted: {1}, method: {2})",
                connection.RemoteIp,
                connection.IsEncrypted,
                connection.EncryptionMethod);

            // Send bitfield (all pieces)
            connection.SendBitfield(torrent.PieceCount);

            // Unchoke
            connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
            connection.AmChoking = false;

            // Handle messages with keep-alive support
            while (connection.IsConnected)
            {
                var message = connection.ReceiveMessage();
                if (message == null)
                {
                    var elapsed = DateTime.UtcNow - connection.LastActivity;
                    if (elapsed.TotalSeconds >= connection.KeepAliveIntervalSeconds)
                    {
                        connection.SendKeepAlive();
                    }

                    continue;
                }

                HandleMessage(connection, message);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Peer connection error: {0}", connection.RemoteIp);
        }
    }

    private bool ValidateInfoHash(byte[] skeyHash)
    {
        var torrents = _torrentService.GetAll();
        return torrents.Any(t =>
        {
            var infoHashBytes = Convert.FromHexString(t.InfoHash);
            var expected = MseKeyDerivation.DeriveKey(infoHashBytes, Encoding.ASCII.GetBytes("req2"));
            return expected.AsSpan().SequenceEqual(skeyHash);
        });
    }

    private void HandleMessage(PeerConnection connection, PeerMessage message)
    {
        switch (message.Type)
        {
            case PeerMessageType.Interested:
                connection.PeerInterested = true;
                if (connection.AmChoking)
                {
                    connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
                    connection.AmChoking = false;
                }

                break;

            case PeerMessageType.NotInterested:
                connection.PeerInterested = false;
                break;

            case PeerMessageType.Request:
                if (Random.Shared.NextDouble() < connection.IdleChance)
                {
                    connection.SendKeepAlive();
                    break;
                }

                if (connection.PendingRequestCount >= connection.MaxPipelinedRequests)
                {
                    _logger.Trace(
                        "Request pipeline full ({0}) from {1}, ignoring",
                        connection.MaxPipelinedRequests,
                        connection.RemoteIp);
                    break;
                }

                if (message.Payload != null && message.Payload.Length >= 12)
                {
                    HandlePieceRequest(connection, message.Payload);
                    connection.PendingRequestCount++;
                }

                break;

            default:
                _logger.Trace("Ignoring message type {0} from {1}", message.Type, connection.RemoteIp);
                break;
        }
    }

    private static void HandlePieceRequest(PeerConnection connection, byte[] payload)
    {
        // Parse request: index (4) + begin (4) + length (4)
        var index = (payload[0] << 24) | (payload[1] << 16) | (payload[2] << 8) | payload[3];
        var begin = (payload[4] << 24) | (payload[5] << 16) | (payload[6] << 8) | payload[7];
        var length = (payload[8] << 24) | (payload[9] << 16) | (payload[10] << 8) | payload[11];

        // Send fake piece data (zeros)
        var piecePayload = new byte[8 + length];
        piecePayload[0] = (byte)(index >> 24);
        piecePayload[1] = (byte)(index >> 16);
        piecePayload[2] = (byte)(index >> 8);
        piecePayload[3] = (byte)index;
        piecePayload[4] = (byte)(begin >> 24);
        piecePayload[5] = (byte)(begin >> 16);
        piecePayload[6] = (byte)(begin >> 8);
        piecePayload[7] = (byte)begin;

        connection.SendMessage(new PeerMessage { Type = PeerMessageType.Piece, Payload = piecePayload });
    }
}
