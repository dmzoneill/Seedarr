using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using NLog;

namespace NzbDrone.Core.Transport;

public enum UtpPacketType : byte
{
    Data = 0,
    Fin = 1,
    State = 2,
    Reset = 3,
    Syn = 4
}

public class UtpHeader
{
    public UtpPacketType Type { get; set; }
    public byte Version { get; set; } = 1;
    public byte Extension { get; set; }
    public ushort ConnectionId { get; set; }
    public uint Timestamp { get; set; }
    public uint TimestampDiff { get; set; }
    public uint WindowSize { get; set; }
    public ushort SequenceNumber { get; set; }
    public ushort AckNumber { get; set; }
}

public interface IUtpConnection : IDisposable
{
    bool IsConnected { get; }
    IPEndPoint RemoteEndPoint { get; }
    void Connect(IPEndPoint endpoint);
    int Send(byte[] data, int offset, int length);
    int Receive(byte[] buffer, int offset, int length);
    Stream GetStream();
    void Flush();
    void Close();
}

public class UtpConnection : IUtpConnection
{
    private const int HeaderSize = 20;
    private const uint DefaultWindowSize = 65535;
    private const int MaxPayloadSize = 1360;

    private readonly UdpClient _udpClient;
    private readonly Logger _logger;
    private readonly int _connectionTimeoutSeconds;
    private readonly ConcurrentDictionary<ushort, byte[]> _outOfOrderBuffer = new();
    private readonly Queue<byte> _receiveQueue = new();
    private readonly object _receiveLock = new();
    private readonly object _socketLock = new();
    private readonly ConcurrentDictionary<ushort, InFlightPacket> _inFlightPackets = new();

    private ushort _connectionId;
    private ushort _sequenceNumber;
    private ushort _ackNumber;
    private IPEndPoint _remoteEndpoint;
    private uint _lastTimestampDiff;
    private uint _remoteWindowSize = DefaultWindowSize;
    private ushort _expectedSeqNr;
    private bool _isDisposed;

    public static Func<uint> MicrosecondProvider { get; set; }
    public Func<byte[], IPEndPoint, bool> PacketDropFilter { get; set; }
    public Action<IUtpConnection> OnClosed { get; set; }

    public bool IsConnected { get; private set; }
    public IPEndPoint RemoteEndPoint => _remoteEndpoint;

    public UtpConnection(int connectionTimeoutSeconds = 30)
    {
        _udpClient = new UdpClient();
        _logger = LogManager.GetCurrentClassLogger();
        _connectionId = (ushort)RandomNumberGenerator.GetInt32(0, ushort.MaxValue + 1);
        _sequenceNumber = 1;
        _connectionTimeoutSeconds = connectionTimeoutSeconds;
    }

    public UtpConnection(UdpClient udpClient, ushort connectionId, IPEndPoint remoteEndpoint, int connectionTimeoutSeconds = 30)
    {
        _udpClient = udpClient ?? new UdpClient();
        _logger = LogManager.GetCurrentClassLogger();
        _connectionId = connectionId;
        _remoteEndpoint = remoteEndpoint;
        _sequenceNumber = 1;
        _connectionTimeoutSeconds = connectionTimeoutSeconds;
        _udpClient.Client.ReceiveTimeout = _connectionTimeoutSeconds * 1000;
        _udpClient.Client.SendTimeout = _connectionTimeoutSeconds * 1000;
    }

    public void Connect(IPEndPoint endpoint)
    {
        if (IsConnected)
        {
            return;
        }

        _remoteEndpoint = endpoint;
        _udpClient.Client.ReceiveTimeout = _connectionTimeoutSeconds * 1000;
        _udpClient.Client.SendTimeout = _connectionTimeoutSeconds * 1000;

        var synPacket = BuildPacket(UtpPacketType.Syn, Array.Empty<byte>());
        var synSeq = _sequenceNumber;

        var rtoMs = 500;
        var maxRetries = Math.Max(1, _connectionTimeoutSeconds * 2);
        var startTime = DateTime.UtcNow;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            if ((DateTime.UtcNow - startTime).TotalSeconds > _connectionTimeoutSeconds)
            {
                break;
            }

            SendUdpPacket(synPacket, synPacket.Length, _remoteEndpoint);

            try
            {
                _udpClient.Client.ReceiveTimeout = rtoMs;
                var receiveEndpoint = new IPEndPoint(IPAddress.Any, 0);
                var response = _udpClient.Receive(ref receiveEndpoint);

                if (response.Length >= HeaderSize)
                {
                    var header = ParseHeader(response);
                    if (header.Type == UtpPacketType.State)
                    {
                        _ackNumber = header.SequenceNumber;
                        _connectionId = (ushort)(header.ConnectionId + 1);
                        _remoteWindowSize = header.WindowSize > 0 ? header.WindowSize : DefaultWindowSize;
                        _expectedSeqNr = header.SequenceNumber;
                        IsConnected = true;
                        _sequenceNumber = (ushort)(synSeq + 1);
                        _logger.Debug("uTP connected to {0}", endpoint);
                        return;
                    }

                    if (header.Type == UtpPacketType.Reset)
                    {
                        _logger.Debug("uTP connection reset by peer {0}", endpoint);
                        return;
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                rtoMs = Math.Min(2000, rtoMs * 2);
            }
        }
    }

    public int Send(byte[] data, int offset, int length)
    {
        if (!IsConnected || length <= 0)
        {
            return 0;
        }

        var totalSent = 0;
        while (totalSent < length && IsConnected)
        {
            var chunkSize = Math.Min(MaxPayloadSize, length - totalSent);
            var payload = new byte[chunkSize];
            Array.Copy(data, offset + totalSent, payload, 0, chunkSize);

            var currentSeq = _sequenceNumber;
            var packet = BuildPacket(UtpPacketType.Data, payload);
            _sequenceNumber++;

            var inFlight = new InFlightPacket
            {
                SequenceNumber = currentSeq,
                PacketData = packet,
                PayloadLength = chunkSize,
                SentTimestamp = Environment.TickCount64,
                Retries = 0
            };

            _inFlightPackets[currentSeq] = inFlight;
            SendUdpPacket(packet, packet.Length, _remoteEndpoint);
            totalSent += chunkSize;

            TryReceiveUdpNonBlocking();
        }

        return totalSent;
    }

    public void Flush()
    {
        var sendStart = DateTime.UtcNow;
        while (!_inFlightPackets.IsEmpty && IsConnected && (DateTime.UtcNow - sendStart).TotalSeconds < _connectionTimeoutSeconds)
        {
            TryReceiveUdpNonBlocking();
            RetransmitUnackedPackets();
            if (!_inFlightPackets.IsEmpty)
            {
                Thread.Sleep(5);
            }
        }
    }

    public int Receive(byte[] buffer, int offset, int length)
    {
        if (!IsConnected || length <= 0)
        {
            return 0;
        }

        lock (_receiveLock)
        {
            if (_receiveQueue.Count > 0)
            {
                var bytesToCopy = Math.Min(_receiveQueue.Count, length);
                for (var i = 0; i < bytesToCopy; i++)
                {
                    buffer[offset + i] = _receiveQueue.Dequeue();
                }

                return bytesToCopy;
            }
        }

        var receiveEndpoint = new IPEndPoint(IPAddress.Any, 0);
        var startTime = DateTime.UtcNow;
        var timeoutMs = _udpClient.Client.ReceiveTimeout > 0 ? _udpClient.Client.ReceiveTimeout : 3000;

        while (IsConnected)
        {
            byte[] data;
            try
            {
                lock (_socketLock)
                {
                    data = _udpClient.Receive(ref receiveEndpoint);
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                RetransmitUnackedPackets();
                if ((DateTime.UtcNow - startTime).TotalMilliseconds >= timeoutMs)
                {
                    return 0;
                }

                continue;
            }
            catch (Exception)
            {
                return 0;
            }

            HandleIncomingPacket(data, receiveEndpoint);
            startTime = DateTime.UtcNow;

            lock (_receiveLock)
            {
                if (_receiveQueue.Count > 0)
                {
                    var bytesToCopy = Math.Min(_receiveQueue.Count, length);
                    for (var i = 0; i < bytesToCopy; i++)
                    {
                        buffer[offset + i] = _receiveQueue.Dequeue();
                    }

                    return bytesToCopy;
                }
            }
        }

        return 0;
    }

    public void HandleIncomingPacket(byte[] data, IPEndPoint sender)
    {
        if (data.Length < HeaderSize)
        {
            return;
        }

        var header = ParseHeader(data);
        if (header == null)
        {
            return;
        }

        _remoteEndpoint ??= sender;
        _remoteWindowSize = header.WindowSize > 0 ? header.WindowSize : DefaultWindowSize;
        if (header.Timestamp > 0)
        {
            _lastTimestampDiff = GetMicroseconds() - header.Timestamp;
        }

        ProcessAck(header.AckNumber);

        if (header.Type == UtpPacketType.Reset)
        {
            IsConnected = false;
            return;
        }

        if (header.Type == UtpPacketType.Fin)
        {
            _ackNumber = header.SequenceNumber;
            var ack = BuildPacket(UtpPacketType.State, Array.Empty<byte>());
            SendUdpPacket(ack, ack.Length, sender);
            IsConnected = false;
            return;
        }

        if (header.Type == UtpPacketType.Syn)
        {
            _ackNumber = header.SequenceNumber;
            _expectedSeqNr = (ushort)(header.SequenceNumber + 1);
            IsConnected = true;
            var ack = BuildPacket(UtpPacketType.State, Array.Empty<byte>());
            SendUdpPacket(ack, ack.Length, sender);
            return;
        }

        if (header.Type == UtpPacketType.Data)
        {
            var payloadLen = data.Length - HeaderSize;
            if (payloadLen > 0)
            {
                var payload = new byte[payloadLen];
                Array.Copy(data, HeaderSize, payload, 0, payloadLen);

                lock (_receiveLock)
                {
                    if (_expectedSeqNr == 0 || header.SequenceNumber == _expectedSeqNr)
                    {
                        _expectedSeqNr = (ushort)(header.SequenceNumber + 1);
                        _ackNumber = header.SequenceNumber;

                        for (var i = 0; i < payloadLen; i++)
                        {
                            _receiveQueue.Enqueue(payload[i]);
                        }

                        while (_outOfOrderBuffer.TryRemove(_expectedSeqNr, out var nextPayload))
                        {
                            _ackNumber = _expectedSeqNr;
                            _expectedSeqNr++;
                            for (var i = 0; i < nextPayload.Length; i++)
                            {
                                _receiveQueue.Enqueue(nextPayload[i]);
                            }
                        }
                    }
                    else if (IsAhead(header.SequenceNumber, _expectedSeqNr))
                    {
                        _outOfOrderBuffer[header.SequenceNumber] = payload;
                    }

                    var ack = BuildPacket(UtpPacketType.State, Array.Empty<byte>());
                    SendUdpPacket(ack, ack.Length, sender);
                }
            }
        }
    }

    public Stream GetStream()
    {
        return new UtpStream(this);
    }

    public void Close()
    {
        Dispose();
    }

    private byte[] BuildPacket(UtpPacketType type, byte[] payload)
    {
        var payloadLen = payload?.Length ?? 0;
        var packet = new byte[HeaderSize + payloadLen];

        packet[0] = (byte)(((byte)type << 4) | 1);
        packet[1] = 0;

        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), _connectionId);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), GetMicroseconds());
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), _lastTimestampDiff);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), DefaultWindowSize);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(16, 2), _sequenceNumber);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2), _ackNumber);

        if (payloadLen > 0)
        {
            Array.Copy(payload, 0, packet, HeaderSize, payloadLen);
        }

        return packet;
    }

    private static UtpHeader ParseHeader(byte[] data)
    {
        if (data == null || data.Length < HeaderSize)
        {
            return null;
        }

        return new UtpHeader
        {
            Type = (UtpPacketType)(data[0] >> 4),
            Version = (byte)(data[0] & 0x0F),
            Extension = data[1],
            ConnectionId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2, 2)),
            Timestamp = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4)),
            TimestampDiff = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8, 4)),
            WindowSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12, 4)),
            SequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(16, 2)),
            AckNumber = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(18, 2))
        };
    }

    private static uint GetMicroseconds()
    {
        if (MicrosecondProvider != null)
        {
            return MicrosecondProvider();
        }

        return (uint)(Environment.TickCount64 * 1000 & 0xFFFFFFFF);
    }

    private void SendUdpPacket(byte[] data, int length, IPEndPoint endpoint)
    {
        if (endpoint == null)
        {
            return;
        }

        if (PacketDropFilter != null && PacketDropFilter(data, endpoint))
        {
            _logger.Trace("Packet dropped by filter ({0} bytes to {1})", length, endpoint);
            return;
        }

        try
        {
            _udpClient.Send(data, length, endpoint);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to send UDP packet to {0}", endpoint);
        }
    }

    private void TryReceiveUdpNonBlocking()
    {
        lock (_socketLock)
        {
            if (_udpClient.Client.Available <= 0)
            {
                return;
            }

            try
            {
                var endpoint = new IPEndPoint(IPAddress.Any, 0);
                var data = _udpClient.Receive(ref endpoint);
                HandleIncomingPacket(data, endpoint);
            }
            catch
            {
                // Non-blocking drain
            }
        }
    }

    private void ProcessAck(ushort ackNr)
    {
        if (_inFlightPackets.IsEmpty)
        {
            return;
        }

        foreach (var key in _inFlightPackets.Keys)
        {
            if (IsAcked(key, ackNr))
            {
                _inFlightPackets.TryRemove(key, out _);
            }
        }
    }

    private void RetransmitUnackedPackets()
    {
        if (_inFlightPackets.IsEmpty || !IsConnected || _remoteEndpoint == null)
        {
            return;
        }

        var now = Environment.TickCount64;
        var unacked = new List<InFlightPacket>(_inFlightPackets.Values);
        unacked.Sort((a, b) => (short)(a.SequenceNumber - b.SequenceNumber));

        var head = unacked[0];
        if (now - head.SentTimestamp >= 50)
        {
            head.Retries++;
            head.SentTimestamp = now;
            SendUdpPacket(head.PacketData, head.PacketData.Length, _remoteEndpoint);
        }
    }

    private static bool IsAhead(ushort seq1, ushort seq2)
    {
        return (ushort)(seq1 - seq2) < 32768;
    }

    private static bool IsAcked(ushort packetSeq, ushort ackSeq)
    {
        return (ushort)(ackSeq - packetSeq) < 32768;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (IsConnected)
        {
            try
            {
                var fin = BuildPacket(UtpPacketType.Fin, Array.Empty<byte>());
                if (_remoteEndpoint != null)
                {
                    _udpClient.Send(fin, fin.Length, _remoteEndpoint);
                }
            }
            catch
            {
                // Best effort
            }

            IsConnected = false;
        }

        _udpClient.Dispose();
        OnClosed?.Invoke(this);
    }

    private class InFlightPacket
    {
        public ushort SequenceNumber { get; set; }
        public byte[] PacketData { get; set; }
        public int PayloadLength { get; set; }
        public long SentTimestamp { get; set; }
        public int Retries { get; set; }
    }
}
