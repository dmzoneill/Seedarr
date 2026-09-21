using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using NLog;
using NzbDrone.Core.Messaging.Events;

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
    event Action<PeerConnection, int, int, int> OnRequestRejected;
    event Action<PeerConnection, int, int, int> RequestRejected;
    HashSet<int> ComputeAllowedFastSet(string ipAddress, byte[] infoHash, int pieceCount, int setSize);
    HashSet<int> ComputeAllowedFastSet(byte[] infoHash, IPAddress peerIp, int totalPieces, int k = 10);
    PeerMessage SerializeHaveAll();
    PeerMessage SerializeHaveNone();
    PeerMessage SerializeSuggestPiece(int pieceIndex);
    PeerMessage SerializeRejectRequest(int pieceIndex, int begin, int length);
    PeerMessage SerializeAllowedFast(int pieceIndex);
    FastMessage Deserialize(PeerMessage message);
    void HandleMessage(PeerConnection connection, PeerMessage message, int pieceCount);
    HashSet<int> GetAllowedFastSet(PeerConnection connection);
    HashSet<int> GetRemoteAllowedFastSet(PeerConnection connection);
    bool IsFastPeer(PeerConnection connection);
    void SendHaveAllOrBitfield(PeerConnection connection, int pieceCount, bool hasAll = true);
    void SendHaveAllOrBitfield(PeerConnection connection, int pieceCount, bool hasAll, bool hasNone);
    void SendHaveAllOrBitfield(PeerConnection connection, int pieceCount, bool hasAll, bool hasNone, byte[] bitfield);
    PeerMessage BuildRejectForRequest(byte[] payload);
    void RegisterFastPeer(PeerConnection connection, byte[] infoHash, int pieceCount, int setSize);
    void UnregisterPeer(PeerConnection connection);
}

public class FastExtensionHandler : IFastExtensionHandler
{
    private const int DefaultFastSetSize = 10;

    private readonly Dictionary<string, HashSet<int>> _localAllowedFastSets = new();
    private readonly Dictionary<string, HashSet<int>> _remoteAllowedFastSets = new();
    private readonly HashSet<string> _fastPeers = new();
    private readonly object _lock = new();
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;

    public event Action<PeerConnection, int, int, int> OnRequestRejected;
    public event Action<PeerConnection, int, int, int> RequestRejected
    {
        add => OnRequestRejected += value;
        remove => OnRequestRejected -= value;
    }

    public FastExtensionHandler(IEventAggregator eventAggregator = null)
    {
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public HashSet<int> ComputeAllowedFastSet(string ipAddress, byte[] infoHash, int pieceCount, int setSize)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var address))
        {
            return new HashSet<int>();
        }

        return ComputeAllowedFastSet(infoHash, address, pieceCount, setSize);
    }

    public HashSet<int> ComputeAllowedFastSet(byte[] infoHash, IPAddress peerIp, int totalPieces, int k = 10)
    {
        // BEP 6 algorithm: generate a deterministic set of allowed-fast piece indices
        // from the peer's IP address and the torrent's infohash.
        //
        // 1. Mask the IP to /24 (IPv4) or /64 (IPv6).
        // 2. x = SHA-1(masked_ip + infohash)
        // 3. For each 4-byte chunk of x, derive a piece index.
        //    When all 5 chunks are consumed, x = SHA-1(x) and repeat.
        var allowedSet = new HashSet<int>();

        if (totalPieces <= 0 || infoHash == null || infoHash.Length == 0 || peerIp == null)
        {
            return allowedSet;
        }

        var ipBytes = MaskIpToSubnet(peerIp);
        if (ipBytes == null)
        {
            return allowedSet;
        }

        if (k <= 0)
        {
            k = DefaultFastSetSize;
        }

        k = Math.Min(k, totalPieces);

        // x = SHA-1(masked_ip + infohash)
        var input = new byte[ipBytes.Length + infoHash.Length];
        Array.Copy(ipBytes, 0, input, 0, ipBytes.Length);
        Array.Copy(infoHash, 0, input, ipBytes.Length, infoHash.Length);

        var x = SHA1.HashData(input);

        while (allowedSet.Count < k)
        {
            for (var i = 0; i < 5 && allowedSet.Count < k; i++)
            {
                var offset = i * 4;
                var index = BinaryPrimitives.ReadUInt32BigEndian(x.AsSpan(offset, 4));
                var pieceIndex = (int)(index % (uint)totalPieces);
                allowedSet.Add(pieceIndex);
            }

            if (allowedSet.Count < k)
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

                var pieceIndex = ReadInt32BigEndian(message.Payload, 0);
                if (pieceIndex < 0)
                {
                    return null;
                }

                return new FastMessage
                {
                    Type = fastType,
                    PieceIndex = pieceIndex
                };

            case FastMessageType.RejectRequest:
                if (message.Payload == null || message.Payload.Length < 12)
                {
                    _logger.Debug("Invalid RejectRequest message: payload too short");
                    return null;
                }

                var rejectPieceIndex = ReadInt32BigEndian(message.Payload, 0);
                var begin = ReadInt32BigEndian(message.Payload, 4);
                var length = ReadInt32BigEndian(message.Payload, 8);
                if (rejectPieceIndex < 0 || begin < 0 || length <= 0)
                {
                    return null;
                }

                return new FastMessage
                {
                    Type = fastType,
                    PieceIndex = rejectPieceIndex,
                    Begin = begin,
                    Length = length
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
                if (pieceCount > 0)
                {
                    connection.PeerPieces = new bool[pieceCount];
                    Array.Fill(connection.PeerPieces, true);
                    connection.HaveCount = pieceCount;
                }
                else if (connection.PeerPieces != null && connection.PeerPieces.Length > 0)
                {
                    Array.Fill(connection.PeerPieces, true);
                    connection.HaveCount = connection.PeerPieces.Length;
                }

                connection.Progress = 1.0;
                break;

            case FastMessageType.HaveNone:
                _logger.Debug("Peer {0} sent HaveNone", connection.RemoteIp);
                if (pieceCount > 0)
                {
                    connection.PeerPieces = new bool[pieceCount];
                }
                else if (connection.PeerPieces != null && connection.PeerPieces.Length > 0)
                {
                    Array.Fill(connection.PeerPieces, false);
                }

                connection.HaveCount = 0;
                connection.Progress = 0.0;
                break;

            case FastMessageType.SuggestPiece:
                _logger.Debug("Peer {0} suggests piece {1}", connection.RemoteIp, fastMessage.PieceIndex);
                lock (connection.SuggestedPieces)
                {
                    connection.SuggestedPieces.Add(fastMessage.PieceIndex);
                }

                break;

            case FastMessageType.AllowedFast:
                connection.SupportsFastExtension = true;
                _logger.Debug("Peer {0} allows fast piece {1}", connection.RemoteIp, fastMessage.PieceIndex);
                lock (connection.RemoteAllowedFastPieces)
                {
                    connection.RemoteAllowedFastPieces.Add(fastMessage.PieceIndex);
                }

                RecordAllowedFastPiece(connection, fastMessage.PieceIndex);
                break;

            case FastMessageType.RejectRequest:
                connection.DecrementPendingRequests();

                _logger.Debug(
                    "Peer {0} rejected request: piece={1} begin={2} length={3}",
                    connection.RemoteIp,
                    fastMessage.PieceIndex,
                    fastMessage.Begin,
                    fastMessage.Length);

                OnRequestRejected?.Invoke(connection, fastMessage.PieceIndex, fastMessage.Begin, fastMessage.Length);
                _eventAggregator?.PublishEvent(new PeerRequestRejectedEvent(connection, fastMessage.PieceIndex, fastMessage.Begin, fastMessage.Length));
                break;
        }
    }

    public HashSet<int> GetAllowedFastSet(PeerConnection connection)
    {
        var key = PeerKey(connection);

        lock (_lock)
        {
            if (_localAllowedFastSets.TryGetValue(key, out var set))
            {
                return new HashSet<int>(set);
            }
        }

        return new HashSet<int>();
    }

    public HashSet<int> GetRemoteAllowedFastSet(PeerConnection connection)
    {
        var key = PeerKey(connection);

        lock (_lock)
        {
            if (_remoteAllowedFastSets.TryGetValue(key, out var set))
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
            _localAllowedFastSets[key] = fastSet;
        }

        _logger.Debug("Registered fast peer {0} with {1} allowed-fast pieces", connection.RemoteIp, fastSet.Count);

        foreach (var pieceIndex in fastSet)
        {
            try
            {
                connection.SendMessage(SerializeAllowedFast(pieceIndex));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to send AllowedFast message to peer {0}", connection.RemoteIp);
                break;
            }
        }
    }

    public void UnregisterPeer(PeerConnection connection)
    {
        var key = PeerKey(connection);

        lock (_lock)
        {
            _fastPeers.Remove(key);
            _localAllowedFastSets.Remove(key);
            _remoteAllowedFastSets.Remove(key);
        }
    }

    public void SendHaveAllOrBitfield(PeerConnection connection, int pieceCount, bool hasAll)
    {
        SendHaveAllOrBitfield(connection, pieceCount, hasAll, false, null);
    }

    public void SendHaveAllOrBitfield(PeerConnection connection, int pieceCount, bool hasAll, bool hasNone)
    {
        SendHaveAllOrBitfield(connection, pieceCount, hasAll, hasNone, null);
    }

    public void SendHaveAllOrBitfield(PeerConnection connection, int pieceCount, bool hasAll, bool hasNone, byte[] bitfield)
    {
        var isFast = IsFastPeer(connection) || (connection?.SupportsFastExtension == true);

        if (isFast && hasAll)
        {
            connection.SendMessage(SerializeHaveAll());
        }
        else if (isFast && hasNone)
        {
            connection.SendMessage(SerializeHaveNone());
        }
        else if (bitfield != null)
        {
            connection.SendBitfield(bitfield);
        }
        else if (hasAll)
        {
            connection.SendBitfield(pieceCount);
        }
        else if (hasNone)
        {
            var byteCount = (pieceCount + 7) / 8;
            connection.SendBitfield(new byte[byteCount]);
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
            if (!_remoteAllowedFastSets.TryGetValue(key, out var set))
            {
                set = new HashSet<int>();
                _remoteAllowedFastSets[key] = set;
            }

            set.Add(pieceIndex);
        }
    }

    internal static byte[] MaskIpToSubnet(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var address))
        {
            return null;
        }

        return MaskIpToSubnet(address);
    }

    internal static byte[] MaskIpToSubnet(IPAddress address)
    {
        if (address == null)
        {
            return null;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            bytes[3] = 0;
            return bytes;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            for (var i = 8; i < 16; i++)
            {
                bytes[i] = 0;
            }

            return bytes;
        }

        return null;
    }

    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, 4), value);
    }

    private static int ReadInt32BigEndian(byte[] buffer, int offset)
    {
        return BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(offset, 4));
    }

    private static string PeerKey(PeerConnection connection)
    {
        return $"{connection.RemoteIp}:{connection.RemotePort}";
    }
}
