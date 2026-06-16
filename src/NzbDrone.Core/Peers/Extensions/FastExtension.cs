using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using NLog;

namespace NzbDrone.Core.Peers.Extensions;

public enum FastMessageType : byte
{
    SuggestPiece = 0x0D,
    HaveAll = 0x0E,
    HaveNone = 0x0F,
    RejectRequest = 0x10,
    AllowedFast = 0x11
}

public class FastMessage
{
    public FastMessageType Type { get; set; }
    public int PieceIndex { get; set; }
    public int Begin { get; set; }
    public int Length { get; set; }
}

public interface IFastExtensionHandler
{
    HashSet<int> ComputeAllowedFastSet(string ipAddress, byte[] infoHash, int pieceCount, int setSize);
    PeerMessage SerializeHaveAll();
    PeerMessage SerializeHaveNone();
    PeerMessage SerializeSuggestPiece(int pieceIndex);
    PeerMessage SerializeRejectRequest(int pieceIndex, int begin, int length);
    PeerMessage SerializeAllowedFast(int pieceIndex);
    FastMessage Deserialize(PeerMessage message);
    void HandleMessage(PeerConnection connection, PeerMessage message, int pieceCount);
    HashSet<int> GetAllowedFastSet(PeerConnection connection);
    bool IsFastPeer(PeerConnection connection);
    void RegisterFastPeer(PeerConnection connection, byte[] infoHash, int pieceCount, int setSize);
    void UnregisterPeer(PeerConnection connection);
}

public class FastExtensionHandler : IFastExtensionHandler
{
    private const int DefaultFastSetSize = 10;

    private readonly Dictionary<string, HashSet<int>> _fastSets = new();
    private readonly HashSet<string> _fastPeers = new();
    private readonly object _lock = new();
    private readonly Logger _logger;

    public FastExtensionHandler()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public HashSet<int> ComputeAllowedFastSet(string ipAddress, byte[] infoHash, int pieceCount, int setSize)
    {
        // BEP 6 algorithm: generate a deterministic set of allowed-fast piece indices
        // from the peer's IP address and the torrent's infohash.
        //
        // 1. Mask the IP to /24 (set last octet to zero).
        // 2. x = SHA-1(masked_ip + infohash)
        // 3. For each 4-byte chunk of x, derive a piece index.
        //    When all 5 chunks are consumed, x = SHA-1(x) and repeat.
        var allowedSet = new HashSet<int>();

        if (pieceCount <= 0)
        {
            return allowedSet;
        }

        if (setSize <= 0)
        {
            setSize = DefaultFastSetSize;
        }

        var ipBytes = MaskIpToSubnet(ipAddress);
        if (ipBytes == null)
        {
            return allowedSet;
        }

        // x = SHA-1(ip[0..3] + infohash)
        var input = new byte[ipBytes.Length + infoHash.Length];
        Array.Copy(ipBytes, 0, input, 0, ipBytes.Length);
        Array.Copy(infoHash, 0, input, ipBytes.Length, infoHash.Length);

        var x = SHA1.HashData(input);

        while (allowedSet.Count < setSize)
        {
            for (var i = 0; i < 5 && allowedSet.Count < setSize; i++)
            {
                var offset = i * 4;
                var index = ((x[offset] << 24) | (x[offset + 1] << 16) |
                             (x[offset + 2] << 8) | x[offset + 3]) & 0x7FFFFFFF;
                var pieceIndex = index % pieceCount;
                allowedSet.Add(pieceIndex);
            }

            if (allowedSet.Count < setSize)
            {
                x = SHA1.HashData(x);
            }
        }

        return allowedSet;
    }

    public PeerMessage SerializeHaveAll()
    {
        return new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.HaveAll,
            Payload = Array.Empty<byte>()
        };
    }

    public PeerMessage SerializeHaveNone()
    {
        return new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.HaveNone,
            Payload = Array.Empty<byte>()
        };
    }

    public PeerMessage SerializeSuggestPiece(int pieceIndex)
    {
        var payload = new byte[4];
        WriteInt32BigEndian(payload, 0, pieceIndex);

        return new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.SuggestPiece,
            Payload = payload
        };
    }

    public PeerMessage SerializeRejectRequest(int pieceIndex, int begin, int length)
    {
        var payload = new byte[12];
        WriteInt32BigEndian(payload, 0, pieceIndex);
        WriteInt32BigEndian(payload, 4, begin);
        WriteInt32BigEndian(payload, 8, length);

        return new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.RejectRequest,
            Payload = payload
        };
    }

    public PeerMessage SerializeAllowedFast(int pieceIndex)
    {
        var payload = new byte[4];
        WriteInt32BigEndian(payload, 0, pieceIndex);

        return new PeerMessage
        {
            Type = (PeerMessageType)FastMessageType.AllowedFast,
            Payload = payload
        };
    }

    public FastMessage Deserialize(PeerMessage message)
    {
        var fastType = (FastMessageType)(byte)message.Type;

        switch (fastType)
        {
            case FastMessageType.HaveAll:
            case FastMessageType.HaveNone:
                return new FastMessage { Type = fastType };

            case FastMessageType.SuggestPiece:
            case FastMessageType.AllowedFast:
                if (message.Payload == null || message.Payload.Length < 4)
                {
                    _logger.Debug("Invalid {0} message: payload too short", fastType);
                    return null;
                }

                return new FastMessage
                {
                    Type = fastType,
                    PieceIndex = ReadInt32BigEndian(message.Payload, 0)
                };

            case FastMessageType.RejectRequest:
                if (message.Payload == null || message.Payload.Length < 12)
                {
                    _logger.Debug("Invalid RejectRequest message: payload too short");
                    return null;
                }

                return new FastMessage
                {
                    Type = fastType,
                    PieceIndex = ReadInt32BigEndian(message.Payload, 0),
                    Begin = ReadInt32BigEndian(message.Payload, 4),
                    Length = ReadInt32BigEndian(message.Payload, 8)
                };

            default:
                _logger.Debug("Unknown fast message type: 0x{0:X2}", (byte)fastType);
                return null;
        }
    }

    public void HandleMessage(PeerConnection connection, PeerMessage message, int pieceCount)
    {
        var fastMessage = Deserialize(message);
        if (fastMessage == null)
        {
            return;
        }

        switch (fastMessage.Type)
        {
            case FastMessageType.HaveAll:
                _logger.Debug("Peer {0} sent HaveAll", connection.RemoteIp);
                break;

            case FastMessageType.HaveNone:
                _logger.Debug("Peer {0} sent HaveNone", connection.RemoteIp);
                break;

            case FastMessageType.SuggestPiece:
                _logger.Debug("Peer {0} suggests piece {1}", connection.RemoteIp, fastMessage.PieceIndex);
                break;

            case FastMessageType.AllowedFast:
                _logger.Debug("Peer {0} allows fast piece {1}", connection.RemoteIp, fastMessage.PieceIndex);
                RecordAllowedFastPiece(connection, fastMessage.PieceIndex);
                break;

            case FastMessageType.RejectRequest:
                _logger.Debug("Peer {0} rejected request: piece={1} begin={2} length={3}",
                    connection.RemoteIp, fastMessage.PieceIndex, fastMessage.Begin, fastMessage.Length);
                break;
        }
    }

    public HashSet<int> GetAllowedFastSet(PeerConnection connection)
    {
        var key = PeerKey(connection);

        lock (_lock)
        {
            if (_fastSets.TryGetValue(key, out var set))
            {
                return new HashSet<int>(set);
            }
        }

        return new HashSet<int>();
    }

    public bool IsFastPeer(PeerConnection connection)
    {
        var key = PeerKey(connection);

        lock (_lock)
        {
            return _fastPeers.Contains(key);
        }
    }

    public void RegisterFastPeer(PeerConnection connection, byte[] infoHash, int pieceCount, int setSize)
    {
        var key = PeerKey(connection);
        var fastSet = ComputeAllowedFastSet(connection.RemoteIp, infoHash, pieceCount, setSize);

        lock (_lock)
        {
            _fastPeers.Add(key);
            _fastSets[key] = fastSet;
        }

        _logger.Debug("Registered fast peer {0} with {1} allowed-fast pieces", connection.RemoteIp, fastSet.Count);

        foreach (var pieceIndex in fastSet)
        {
            connection.SendMessage(SerializeAllowedFast(pieceIndex));
        }
    }

    public void UnregisterPeer(PeerConnection connection)
    {
        var key = PeerKey(connection);

        lock (_lock)
        {
            _fastPeers.Remove(key);
            _fastSets.Remove(key);
        }
    }

    public void SendHaveAllOrBitfield(PeerConnection connection, int pieceCount, bool allPiecesAvailable)
    {
        if (IsFastPeer(connection) && allPiecesAvailable)
        {
            connection.SendMessage(SerializeHaveAll());
        }
        else if (IsFastPeer(connection) && pieceCount > 0 && !allPiecesAvailable)
        {
            connection.SendBitfield(pieceCount);
        }
        else
        {
            connection.SendBitfield(pieceCount);
        }
    }

    public PeerMessage BuildRejectForRequest(byte[] requestPayload)
    {
        if (requestPayload == null || requestPayload.Length < 12)
        {
            return null;
        }

        var pieceIndex = ReadInt32BigEndian(requestPayload, 0);
        var begin = ReadInt32BigEndian(requestPayload, 4);
        var length = ReadInt32BigEndian(requestPayload, 8);

        return SerializeRejectRequest(pieceIndex, begin, length);
    }

    private void RecordAllowedFastPiece(PeerConnection connection, int pieceIndex)
    {
        var key = PeerKey(connection);

        lock (_lock)
        {
            if (!_fastSets.TryGetValue(key, out var set))
            {
                set = new HashSet<int>();
                _fastSets[key] = set;
            }

            set.Add(pieceIndex);
        }
    }

    private static byte[] MaskIpToSubnet(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out var address))
        {
            return null;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return null;
        }

        var bytes = address.GetAddressBytes();
        bytes[3] = 0;
        return bytes;
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static int ReadInt32BigEndian(byte[] buffer, int offset)
    {
        return (buffer[offset] << 24) | (buffer[offset + 1] << 16) |
               (buffer[offset + 2] << 8) | buffer[offset + 3];
    }

    private static string PeerKey(PeerConnection connection)
    {
        return $"{connection.RemoteIp}:{connection.RemotePort}";
    }
}
