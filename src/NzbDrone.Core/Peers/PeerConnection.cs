using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NLog;
using NzbDrone.Core.Peers.Encryption;

namespace NzbDrone.Core.Peers;

public class PeerConnection : IDisposable
{
    private const string ProtocolString = "BitTorrent protocol";

    private readonly TcpClient _client;
    private readonly NetworkStream _networkStream;
    private readonly Logger _logger;
    private Stream _activeStream;

    public string RemoteIp { get; }
    public int RemotePort { get; }
    public string InfoHash { get; private set; }
    public string PeerId { get; private set; }
    public bool IsConnected => _client?.Connected ?? false;
    public bool IsEncrypted { get; private set; }
    public CryptoMethod EncryptionMethod { get; private set; }
    public bool AmChoking { get; set; } = true;
    public bool AmInterested { get; set; }
    public bool PeerChoking { get; set; } = true;
    public bool PeerInterested { get; set; }
    public DateTime ConnectedAt { get; }
    public DateTime LastActivity { get; set; }

    public PeerConnection(TcpClient client)
    {
        _client = client;
        _networkStream = client.GetStream();
        _activeStream = _networkStream;
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
        _networkStream = _client.GetStream();
        _activeStream = _networkStream;
        _logger = LogManager.GetCurrentClassLogger();
        RemoteIp = host;
        RemotePort = port;
        ConnectedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
    }

    public bool NegotiateEncryptionOutgoing(string infoHash, EncryptionMode mode)
    {
        try
        {
            var infoHashBytes = Convert.FromHexString(infoHash);
            var handshake = new MseHandshake(infoHashBytes, mode);
            _activeStream = handshake.NegotiateOutgoing(_networkStream);
            EncryptionMethod = handshake.NegotiatedMethod;
            IsEncrypted = EncryptionMethod == CryptoMethod.Rc4;
            InfoHash = infoHash;
            LastActivity = DateTime.UtcNow;
            _logger.Debug("MSE/PE outgoing completed with {0}:{1} - method: {2}", RemoteIp, RemotePort, EncryptionMethod);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "MSE/PE outgoing negotiation failed with {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public bool NegotiateEncryptionIncoming(Func<byte[], bool> infoHashValidator, EncryptionMode mode)
    {
        try
        {
            // Peek the first byte to detect whether this is an MSE or plain handshake.
            // A standard BT handshake starts with 0x13 (19); MSE starts with the DH public key.
            var peek = new byte[1];
            var read = _networkStream.Read(peek, 0, 1);
            if (read == 0)
            {
                return false;
            }

            if (peek[0] == 19 && mode != EncryptionMode.RequireEncrypted)
            {
                // Plain BitTorrent handshake - feed the peeked byte back through a PrefixedStream
                _activeStream = new PrefixedStream(peek, _networkStream);
                EncryptionMethod = CryptoMethod.PlainText;
                IsEncrypted = false;
                return true;
            }

            // MSE/PE handshake - prefix the peeked byte back
            var prefixed = new PrefixedStream(peek, _networkStream);
            var handshake = new MseHandshake(Array.Empty<byte>(), mode);
            _activeStream = handshake.NegotiateIncoming(prefixed, infoHashValidator);
            EncryptionMethod = handshake.NegotiatedMethod;
            IsEncrypted = EncryptionMethod == CryptoMethod.Rc4;
            LastActivity = DateTime.UtcNow;
            _logger.Debug("MSE/PE incoming completed with {0}:{1} - method: {2}", RemoteIp, RemotePort, EncryptionMethod);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "MSE/PE incoming negotiation failed with {0}:{1}", RemoteIp, RemotePort);
            return false;
        }
    }

    public bool SendHandshake(string infoHash, string peerId)
    {
        try
        {
            var handshake = BuildHandshake(infoHash, peerId);
            _activeStream.Write(handshake, 0, handshake.Length);
            _activeStream.Flush();
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

        _activeStream.Write(buffer, 0, buffer.Length);
        _activeStream.Flush();
        LastActivity = DateTime.UtcNow;
    }

    public void SendKeepAlive()
    {
        var buffer = new byte[4];
        _activeStream.Write(buffer, 0, 4);
        _activeStream.Flush();
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
            var read = _activeStream.Read(buffer, offset, count - offset);
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
        if (_activeStream != _networkStream)
        {
            _activeStream?.Dispose();
        }

        _networkStream?.Dispose();
        _client?.Dispose();
    }
}
