using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Peers;

public class PeerServer : BackgroundService
{
    private const int DefaultPort = 6881;

    private readonly ITorrentService _torrentService;
    private readonly Logger _logger;

    public PeerServer(ITorrentService torrentService)
    {
        _torrentService = torrentService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, DefaultPort);

        try
        {
            listener.Start();
            _logger.Info("Peer server listening on port {0}", DefaultPort);

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

    private void HandleConnection(TcpClient client)
    {
        using var connection = new PeerConnection(client);
        _logger.Debug("Incoming peer: {0}:{1}", connection.RemoteIp, connection.RemotePort);

        try
        {
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

            // Send bitfield (all pieces)
            connection.SendBitfield(torrent.PieceCount);

            // Unchoke
            connection.SendMessage(new PeerMessage { Type = PeerMessageType.Unchoke });
            connection.AmChoking = false;

            // Handle messages
            while (connection.IsConnected)
            {
                var message = connection.ReceiveMessage();
                if (message == null)
                {
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
                if (message.Payload != null && message.Payload.Length >= 12)
                {
                    HandlePieceRequest(connection, message.Payload);
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
