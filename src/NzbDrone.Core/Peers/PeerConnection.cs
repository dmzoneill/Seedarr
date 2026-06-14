using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NLog;

namespace NzbDrone.Core.Peers;

public class PeerConnection : IDisposable
{
    private const string ProtocolString = "BitTorrent protocol";

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Logger _logger;

    public string RemoteIp { get; }
    public int RemotePort { get; }
    public string InfoHash { get; private set; }
    public string PeerId { get; private set; }
    public bool IsConnected => _client?.Connected ?? false;
    public bool AmChoking { get; set; } = true;
    public bool AmInterested { get; set; }
    public bool PeerChoking { get; set; } = true;
    public bool PeerInterested { get; set; }
    public DateTime ConnectedAt { get; }
    public DateTime LastActivity { get; set; }

    public PeerConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _logger = LogManager.GetCurrentClassLogger();
        var endpoint = (IPEndPoint)client.Client.RemoteEndPoint;
        RemoteIp = endpoint.Address.ToString();
        RemotePort = endpoint.Port;
        ConnectedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
    }

    public PeerConnection(string host, int port)
    {
        _client = new TcpClient();
        _client.Connect(host, port);
        _stream = _client.GetStream();
        _logger = LogManager.GetCurrentClassLogger();
        RemoteIp = host;
        RemotePort = port;
        ConnectedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
    }

    public bool SendHandshake(string infoHash, string peerId)
    {
        try
        {
            var handshake = BuildHandshake(infoHash, peerId);
            _stream.Write(handshake, 0, handshake.Length);
            InfoHash = infoHash;
            PeerId = peerId;
            LastActivity = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Handshake send failed to {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public bool ReceiveHandshake()
    {
        try
        {
            var buffer = new byte[68];
            var read = ReadExact(buffer, 68);
            if (!read)
            {
                return false;
            }

            var pstrlen = buffer[0];
            if (pstrlen != 19)
            {
                return false;
            }

            var pstr = Encoding.ASCII.GetString(buffer, 1, 19);
            if (pstr != ProtocolString)
            {
                return false;
            }

            // reserved bytes at 20-27
            InfoHash = Convert.ToHexString(buffer, 28, 20).ToLowerInvariant();
            PeerId = Encoding.ASCII.GetString(buffer, 48, 20);
            LastActivity = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Handshake receive failed from {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public void SendMessage(PeerMessage message)
    {
        var length = message.Length;
        var buffer = new byte[4 + length];
        buffer[0] = (byte)(length >> 24);
        buffer[1] = (byte)(length >> 16);
        buffer[2] = (byte)(length >> 8);
        buffer[3] = (byte)length;
        buffer[4] = (byte)message.Type;

        if (message.Payload != null && message.Payload.Length > 0)
        {
            Array.Copy(message.Payload, 0, buffer, 5, message.Payload.Length);
        }

        _stream.Write(buffer, 0, buffer.Length);
        LastActivity = DateTime.UtcNow;
    }

    public void SendKeepAlive()
    {
        var buffer = new byte[4];
        _stream.Write(buffer, 0, 4);
        LastActivity = DateTime.UtcNow;
    }

    public PeerMessage ReceiveMessage()
    {
        var lengthBuffer = new byte[4];
        if (!ReadExact(lengthBuffer, 4))
        {
            return null;
        }

        var length = (lengthBuffer[0] << 24) | (lengthBuffer[1] << 16) |
                     (lengthBuffer[2] << 8) | lengthBuffer[3];

        if (length == 0)
        {
            return null; // keep-alive
        }

        var messageBuffer = new byte[length];
        if (!ReadExact(messageBuffer, length))
        {
            return null;
        }

        var message = new PeerMessage
        {
            Type = (PeerMessageType)messageBuffer[0]
        };

        if (length > 1)
        {
            message.Payload = new byte[length - 1];
            Array.Copy(messageBuffer, 1, message.Payload, 0, length - 1);
        }

        LastActivity = DateTime.UtcNow;
        return message;
    }

    public void SendBitfield(int pieceCount)
    {
        // Send full bitfield (all pieces available - we're a seeder)
        var byteCount = (pieceCount + 7) / 8;
        var bitfield = new byte[byteCount];
        for (var i = 0; i < byteCount; i++)
        {
            bitfield[i] = 0xFF;
        }

        // Clear trailing bits in the last byte
        var spare = (byteCount * 8) - pieceCount;
        if (spare > 0)
        {
            bitfield[byteCount - 1] = (byte)(0xFF << spare);
        }

        SendMessage(new PeerMessage { Type = PeerMessageType.Bitfield, Payload = bitfield });
    }

    private static byte[] BuildHandshake(string infoHash, string peerId)
    {
        var buffer = new byte[68];
        buffer[0] = 19;
        Encoding.ASCII.GetBytes(ProtocolString, 0, 19, buffer, 1);

        // reserved bytes 20-27 are zero
        var hashBytes = Convert.FromHexString(infoHash);
        Array.Copy(hashBytes, 0, buffer, 28, 20);
        Encoding.ASCII.GetBytes(peerId.PadRight(20)[..20], 0, 20, buffer, 48);
        return buffer;
    }

    private bool ReadExact(byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = _stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }
}
