using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using NLog;
using NzbDrone.Core.Network;

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
    ushort ReceiveId { get; }
    ushort SendId { get; }
    void Connect(IPEndPoint endpoint);
    int Send(byte[] data, int offset, int length);
    int Receive(byte[] buffer, int offset, int length);
    Stream GetStream();
    void Flush();
    void Close();
}

public class UtpConnection : IUtpConnection
{
    public const uint MaxBufferSize = 1024 * 1024;
    private const int HeaderSize = 20;
    private const uint DefaultWindowSize = 65535;
    private const int MaxPayloadSize = 1360;

    private readonly UdpClient _udpClient;
    private readonly bool _ownsUdpClient;
    private readonly Logger _logger;
    private readonly int _connectionTimeoutSeconds;
    private readonly ConcurrentDictionary<ushort, byte[]> _outOfOrderBuffer = new();
    private readonly Queue<byte> _receiveQueue = new();
    private readonly object _receiveLock = new();
    private readonly object _socketReceiveLock = new();
    private readonly object _sendLock = new();
    private readonly ManualResetEventSlim _ackReceivedEvent = new(false);
    private readonly ConcurrentDictionary<ushort, InFlightPacket> _inFlightPackets = new();

    private ushort _connectionId;
    private ushort _sequenceNumber;
    private ushort _ackNumber;
    private IPEndPoint _remoteEndpoint;
    private uint _lastTimestampDiff;
    private uint _remoteWindowSize = DefaultWindowSize;
    private ushort _expectedSeqNr;
    private ushort _lastAckReceived;
    private int _duplicateAckCount;
    private int _fastRetransmitCount;
    private bool _hasReceivedFirstAck;
    private bool _hasReceivedFirstPacket;
    private bool _hasReceivedFin;
    private bool _isClosing;
    private bool _isDisposed;

    public static Func<uint> MicrosecondProvider { get; set; }
    public Func<byte[], IPEndPoint, bool> PacketDropFilter { get; set; }
    public Action<IUtpConnection> OnClosed { get; set; }
    public Action<IUtpConnection, IPEndPoint> OnConnecting { get; set; }
    public Action<IUtpConnection> OnConnected { get; set; }

    public bool IsConnected { get; private set; }
    public bool HasReceivedFirstPacket => _hasReceivedFirstPacket;
    public bool HasReceivedFin => _hasReceivedFin;
    public bool IsClosing => _isClosing;
    public int OutOfOrderCount => _outOfOrderBuffer.Count;
    public int DuplicateAckCount => _duplicateAckCount;
    public int FastRetransmitCount => _fastRetransmitCount;
    public ushort LastAckReceived => _lastAckReceived;
    public uint RemoteWindowSize { get => _remoteWindowSize; internal set => _remoteWindowSize = value; }
    public IPEndPoint RemoteEndPoint => _remoteEndpoint;
    public bool OwnsUdpClient => _ownsUdpClient;
    public ushort ReceiveId { get; private set; }
    public ushort SendId => _connectionId;

    public UtpConnection(int connectionTimeoutSeconds = 30, string bindInterface = null, IPAddress localIp = null)
    {
        _udpClient = new UdpClient();
        _udpClient.Client.BindToNetworkInterface(bindInterface, localIp);
        _ownsUdpClient = true;
        _logger = LogManager.GetCurrentClassLogger();
        _connectionId = (ushort)RandomNumberGenerator.GetInt32(0, ushort.MaxValue + 1);
        ReceiveId = _connectionId;
        _sequenceNumber = 1;
        _connectionTimeoutSeconds = connectionTimeoutSeconds;
    }

    public UtpConnection(UdpClient udpClient, ushort connectionId, IPEndPoint remoteEndpoint, int connectionTimeoutSeconds = 30, string bindInterface = null, IPAddress localIp = null)
    {
        if (udpClient == null)
        {
            _udpClient = new UdpClient();
            _udpClient.Client.BindToNetworkInterface(bindInterface, localIp);
            _ownsUdpClient = true;
        }
        else
        {
            _udpClient = udpClient;
            _ownsUdpClient = false;
        }

        _logger = LogManager.GetCurrentClassLogger();
        _connectionId = connectionId;
        ReceiveId = (ushort)(connectionId + 1);
        _remoteEndpoint = remoteEndpoint;
        _sequenceNumber = 1;
        _connectionTimeoutSeconds = connectionTimeoutSeconds;

        if (_ownsUdpClient)
        {
            try
            {
                _udpClient.Client.ReceiveTimeout = _connectionTimeoutSeconds * 1000;
                _udpClient.Client.SendTimeout = _connectionTimeoutSeconds * 1000;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to set socket timeouts");
            }
        }
    }

    public void Connect(IPEndPoint endpoint)
    {
        if (IsConnected)
        {
            return;
        }

        _remoteEndpoint = endpoint;
        OnConnecting?.Invoke(this, endpoint);

        if (_ownsUdpClient)
        {
            try
            {
                _udpClient.Client.ReceiveTimeout = _connectionTimeoutSeconds * 1000;
                _udpClient.Client.SendTimeout = _connectionTimeoutSeconds * 1000;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to set socket timeouts");
            }
        }

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
                if (_ownsUdpClient)
                {
                    try
                    {
                        _udpClient.Client.ReceiveTimeout = rtoMs;
                    }
                    catch
                    {
                    }
                }

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
                        OnConnected?.Invoke(this);
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
        if (!IsConnected || _isClosing || length <= 0)
        {
            return 0;
        }

        lock (_sendLock)
        {
            if (!IsConnected || _isClosing)
            {
                return 0;
            }

            var totalSent = 0;
            while (totalSent < length && IsConnected && !_isClosing)
            {
                var sendWaitStart = DateTime.UtcNow;
                var currentInFlightBytes = _inFlightPackets.Values.Sum(p => p.PayloadLength);
                while (currentInFlightBytes >= _remoteWindowSize && IsConnected && !_isClosing)
                {
                    if (_connectionTimeoutSeconds > 0 && (DateTime.UtcNow - sendWaitStart).TotalSeconds >= _connectionTimeoutSeconds)
                    {
                        break;
                    }

                    TryReceiveUdpNonBlocking();
                    RetransmitUnackedPackets();
                    Thread.Sleep(2);
                    currentInFlightBytes = _inFlightPackets.Values.Sum(p => p.PayloadLength);
                }

                if (!IsConnected || _isClosing || (currentInFlightBytes >= _remoteWindowSize && totalSent > 0))
                {
                    break;
                }

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
    }

    public void Flush()
    {
        var sendStart = DateTime.UtcNow;
        while (!_inFlightPackets.IsEmpty && (IsConnected || _isClosing) && (DateTime.UtcNow - sendStart).TotalSeconds < _connectionTimeoutSeconds)
        {
            TryReceiveUdpNonBlocking();
            RetransmitUnackedPackets();
            if (_inFlightPackets.IsEmpty)
            {
                break;
            }

            var elapsedMs = (DateTime.UtcNow - sendStart).TotalMilliseconds;
            var remainingTimeoutMs = (_connectionTimeoutSeconds * 1000) - elapsedMs;
            if (remainingTimeoutMs <= 0)
            {
                break;
            }

            var waitMs = (int)Math.Min(50, remainingTimeoutMs);
            _ackReceivedEvent.Reset();
            if (_inFlightPackets.IsEmpty)
            {
                break;
            }

            _ackReceivedEvent.Wait(waitMs);
        }
    }

    public int Receive(byte[] buffer, int offset, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        var startTime = DateTime.UtcNow;
        var timeoutMs = _ownsUdpClient && _udpClient.Client.ReceiveTimeout > 0
            ? _udpClient.Client.ReceiveTimeout
            : (_connectionTimeoutSeconds > 0 ? _connectionTimeoutSeconds * 1000 : 3000);

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

            if (_hasReceivedFin || !IsConnected)
            {
                return 0;
            }

            if (!_ownsUdpClient)
            {
                while (IsConnected && !_hasReceivedFin && _receiveQueue.Count == 0)
                {
                    if (_udpClient?.Client != null && _udpClient.Client.Available > 0)
                    {
                        try
                        {
                            var endpoint = new IPEndPoint(IPAddress.Any, 0);
                            byte[] packet;
                            lock (_socketReceiveLock)
                            {
                                packet = _udpClient.Receive(ref endpoint);
                            }

                            HandleIncomingPacket(packet, endpoint);
                            if (_receiveQueue.Count > 0)
                            {
                                break;
                            }
                        }
                        catch
                        {
                        }
                    }

                    var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    if (elapsed >= timeoutMs)
                    {
                        return 0;
                    }

                    var waitTime = (int)Math.Min(100, timeoutMs - elapsed);
                    if (waitTime <= 0)
                    {
                        return 0;
                    }

                    Monitor.Wait(_receiveLock, waitTime);
                }

                if (_receiveQueue.Count > 0)
                {
                    var bytesToCopy = Math.Min(_receiveQueue.Count, length);
                    for (var i = 0; i < bytesToCopy; i++)
                    {
                        buffer[offset + i] = _receiveQueue.Dequeue();
                    }

                    return bytesToCopy;
                }

                return 0;
            }
        }

        var receiveEndpoint = new IPEndPoint(IPAddress.Any, 0);

        while (IsConnected && !_hasReceivedFin)
        {
            byte[] data;
            try
            {
                lock (_socketReceiveLock)
                {
                    if (!IsConnected || _hasReceivedFin)
                    {
                        break;
                    }

                    data = _udpClient.Receive(ref receiveEndpoint);
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                RetransmitUnackedPackets();
                if ((DateTime.UtcNow - startTime).TotalMilliseconds >= timeoutMs)
                {
                    break;
                }

                continue;
            }
            catch (Exception)
            {
                break;
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

                if (_hasReceivedFin)
                {
                    return 0;
                }
            }
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
        _remoteWindowSize = (header.Type == UtpPacketType.Syn && header.WindowSize == 0) ? DefaultWindowSize : header.WindowSize;
        if (header.Timestamp > 0)
        {
            _lastTimestampDiff = GetMicroseconds() - header.Timestamp;
        }

        ProcessAck(header.AckNumber);

        var payloadOffset = HeaderSize;
        if (header.Extension != 0)
        {
            var extOffset = HeaderSize;
            var currentExt = header.Extension;

            while (currentExt != 0 && extOffset + 2 <= data.Length)
            {
                var nextExt = data[extOffset];
                var extLen = data[extOffset + 1];
                extOffset += 2;

                if (extOffset + extLen > data.Length)
                {
                    extOffset = data.Length;
                    break;
                }

                if (currentExt == 1)
                {
                    ProcessSackBitmask(header.AckNumber, data.AsSpan(extOffset, extLen));
                }

                extOffset += extLen;
                currentExt = nextExt;
            }

            payloadOffset = extOffset;
        }

        if (header.Type == UtpPacketType.Reset)
        {
            IsConnected = false;
            lock (_receiveLock)
            {
                Monitor.PulseAll(_receiveLock);
            }

            return;
        }

        if (header.Type == UtpPacketType.Fin)
        {
            _ackNumber = header.SequenceNumber;
            var ack = BuildPacket(UtpPacketType.State, Array.Empty<byte>());
            SendUdpPacket(ack, ack.Length, sender);
            lock (_receiveLock)
            {
                _hasReceivedFin = true;
                Monitor.PulseAll(_receiveLock);
            }

            return;
        }

        if (header.Type == UtpPacketType.Syn)
        {
            _ackNumber = header.SequenceNumber;
            _expectedSeqNr = (ushort)(header.SequenceNumber + 1);
            _hasReceivedFirstPacket = true;
            IsConnected = true;
            var ack = BuildPacket(UtpPacketType.State, Array.Empty<byte>());
            SendUdpPacket(ack, ack.Length, sender);
            return;
        }

        if (header.Type == UtpPacketType.Data)
        {
            var payloadLen = Math.Max(0, data.Length - payloadOffset);
            lock (_receiveLock)
            {
                if (payloadLen > 0)
                {
                    var payload = data.AsSpan(payloadOffset, payloadLen).ToArray();

                    if (!_hasReceivedFirstPacket)
                    {
                        _hasReceivedFirstPacket = true;
                        _expectedSeqNr = (ushort)(header.SequenceNumber + 1);
                        _ackNumber = header.SequenceNumber;

                        for (var i = 0; i < payloadLen; i++)
                        {
                            _receiveQueue.Enqueue(payload[i]);
                        }

                        Monitor.PulseAll(_receiveLock);
                    }
                    else if (header.SequenceNumber == _expectedSeqNr)
                    {
                        _expectedSeqNr = (ushort)(_expectedSeqNr + 1);
                        _ackNumber = header.SequenceNumber;

                        for (var i = 0; i < payloadLen; i++)
                        {
                            _receiveQueue.Enqueue(payload[i]);
                        }

                        while (_outOfOrderBuffer.TryRemove(_expectedSeqNr, out var nextPayload))
                        {
                            _ackNumber = _expectedSeqNr;
                            _expectedSeqNr = (ushort)(_expectedSeqNr + 1);
                            for (var i = 0; i < nextPayload.Length; i++)
                            {
                                _receiveQueue.Enqueue(nextPayload[i]);
                            }
                        }

                        Monitor.PulseAll(_receiveLock);
                    }
                    else if (IsAhead(header.SequenceNumber, _expectedSeqNr))
                    {
                        _outOfOrderBuffer[header.SequenceNumber] = payload;
                    }
                }

                var ack = BuildPacket(UtpPacketType.State, Array.Empty<byte>());
                SendUdpPacket(ack, ack.Length, sender);
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

        uint bufferedBytes;
        lock (_receiveLock)
        {
            bufferedBytes = (uint)_receiveQueue.Count;
        }

        var advertisedWnd = bufferedBytes >= MaxBufferSize ? 0 : MaxBufferSize - bufferedBytes;

        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), _connectionId);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), GetMicroseconds());
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), _lastTimestampDiff);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), advertisedWnd);
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
        if (!_ownsUdpClient)
        {
            return;
        }

        if (!Monitor.TryEnter(_socketReceiveLock))
        {
            return;
        }

        try
        {
            if (_udpClient.Client == null || (!_udpClient.Client.Connected && !_udpClient.Client.IsBound))
            {
                return;
            }

            while (_udpClient.Client.Available > 0)
            {
                var endpoint = new IPEndPoint(IPAddress.Any, 0);
                var data = _udpClient.Receive(ref endpoint);
                HandleIncomingPacket(data, endpoint);
            }
        }
        catch
        {
            // Non-blocking drain
        }
        finally
        {
            Monitor.Exit(_socketReceiveLock);
        }
    }

    private void ProcessAck(ushort ackNr)
    {
        if (!_hasReceivedFirstAck)
        {
            _hasReceivedFirstAck = true;
            _lastAckReceived = ackNr;
            _duplicateAckCount = 0;
        }
        else if (ackNr == _lastAckReceived)
        {
            if (!_inFlightPackets.IsEmpty)
            {
                _duplicateAckCount++;
                if (_duplicateAckCount == 3)
                {
                    var lostSeq = (ushort)(ackNr + 1);
                    if (_inFlightPackets.TryGetValue(lostSeq, out var lostPacket) && !lostPacket.FastRetransmitted)
                    {
                        _logger.Debug("Fast Retransmit triggered for packet {0} on 3 duplicate ACKs", lostSeq);
                        lostPacket.FastRetransmitted = true;
                        lostPacket.Retries++;
                        lostPacket.SentTimestamp = Environment.TickCount64;
                        SendUdpPacket(lostPacket.PacketData, lostPacket.PacketData.Length, _remoteEndpoint);
                        _fastRetransmitCount++;
                    }
                }
            }

            return;
        }
        else if (IsAhead(ackNr, _lastAckReceived))
        {
            _lastAckReceived = ackNr;
            _duplicateAckCount = 0;
        }

        if (_inFlightPackets.IsEmpty)
        {
            return;
        }

        var anyRemoved = false;
        foreach (var key in _inFlightPackets.Keys)
        {
            if (IsAcked(key, ackNr))
            {
                if (_inFlightPackets.TryRemove(key, out _))
                {
                    anyRemoved = true;
                }
            }
        }

        if (anyRemoved)
        {
            _ackReceivedEvent.Set();
        }
    }

    private void ProcessSackBitmask(ushort ackNr, ReadOnlySpan<byte> bitmask)
    {
        var anyRemoved = false;
        var newlyAckedSeqNrs = new List<ushort>();

        for (var byteIdx = 0; byteIdx < bitmask.Length; byteIdx++)
        {
            var b = bitmask[byteIdx];
            for (var bitIdx = 0; bitIdx < 8; bitIdx++)
            {
                if ((b & (1 << bitIdx)) != 0)
                {
                    var sackSeq = (ushort)(ackNr + 2 + (byteIdx * 8) + bitIdx);
                    if (_inFlightPackets.TryRemove(sackSeq, out _))
                    {
                        anyRemoved = true;
                        newlyAckedSeqNrs.Add(sackSeq);
                        _logger.Trace("SACK acknowledged in-flight packet {0}", sackSeq);
                    }
                }
            }
        }

        if (anyRemoved)
        {
            _ackReceivedEvent.Set();
        }

        if (newlyAckedSeqNrs.Count > 0 && !_inFlightPackets.IsEmpty)
        {
            CheckSackFastRetransmit(newlyAckedSeqNrs);
        }
    }

    private void CheckSackFastRetransmit(List<ushort> newlyAckedSeqNrs)
    {
        if (_remoteEndpoint == null)
        {
            return;
        }

        var packetsToRetransmit = new List<InFlightPacket>();

        foreach (var inFlight in _inFlightPackets.Values)
        {
            foreach (var sackSeq in newlyAckedSeqNrs)
            {
                if (IsAhead(sackSeq, inFlight.SequenceNumber))
                {
                    inFlight.SackAckedHigherCount++;
                }
            }

            if (inFlight.SackAckedHigherCount >= 3 && !inFlight.FastRetransmitted)
            {
                inFlight.FastRetransmitted = true;
                packetsToRetransmit.Add(inFlight);
            }
        }

        if (packetsToRetransmit.Count > 0)
        {
            packetsToRetransmit.Sort((a, b) => (short)(a.SequenceNumber - b.SequenceNumber));
            var now = Environment.TickCount64;
            foreach (var packet in packetsToRetransmit)
            {
                _logger.Debug("Fast Retransmit triggered for packet {0} on SACK", packet.SequenceNumber);
                packet.Retries++;
                packet.SentTimestamp = now;
                SendUdpPacket(packet.PacketData, packet.PacketData.Length, _remoteEndpoint);
                _fastRetransmitCount++;
            }
        }
    }

    private void RetransmitUnackedPackets()
    {
        if (_inFlightPackets.IsEmpty || (!IsConnected && !_isClosing) || _remoteEndpoint == null)
        {
            return;
        }

        var now = Environment.TickCount64;
        var unacked = new List<InFlightPacket>(_inFlightPackets.Values);
        if (unacked.Count == 0)
        {
            return;
        }

        unacked.Sort((a, b) => (short)(a.SequenceNumber - b.SequenceNumber));

        var head = unacked[0];
        if (now - head.SentTimestamp >= 200)
        {
            head.Retries++;
            head.SentTimestamp = now;
            SendUdpPacket(head.PacketData, head.PacketData.Length, _remoteEndpoint);
        }
    }

    private void WaitForFinAck(ushort finSeq)
    {
        var startTime = DateTime.UtcNow;
        var maxWaitMs = Math.Min(300, _connectionTimeoutSeconds > 0 ? _connectionTimeoutSeconds * 1000 : 300);

        while (_inFlightPackets.ContainsKey(finSeq) && (DateTime.UtcNow - startTime).TotalMilliseconds < maxWaitMs)
        {
            TryReceiveUdpNonBlocking();

            if (!_inFlightPackets.ContainsKey(finSeq))
            {
                break;
            }

            RetransmitUnackedPackets();

            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var remainingMs = maxWaitMs - elapsedMs;
            if (remainingMs <= 0)
            {
                break;
            }

            var waitMs = (int)Math.Min(50, remainingMs);
            _ackReceivedEvent.Reset();

            if (!_inFlightPackets.ContainsKey(finSeq))
            {
                break;
            }

            _ackReceivedEvent.Wait(waitMs);
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
            _isClosing = true;
            IsConnected = false;

            try
            {
                if (_remoteEndpoint != null)
                {
                    ushort finSeq;
                    byte[] finPacket;
                    lock (_sendLock)
                    {
                        finSeq = _sequenceNumber;
                        finPacket = BuildPacket(UtpPacketType.Fin, Array.Empty<byte>());
                        _sequenceNumber++;
                    }

                    var inFlight = new InFlightPacket
                    {
                        SequenceNumber = finSeq,
                        PacketData = finPacket,
                        PayloadLength = 0,
                        SentTimestamp = Environment.TickCount64,
                        Retries = 0
                    };

                    _inFlightPackets[finSeq] = inFlight;
                    SendUdpPacket(finPacket, finPacket.Length, _remoteEndpoint);

                    WaitForFinAck(finSeq);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error during graceful FIN teardown");
            }
            finally
            {
                _isClosing = false;
            }
        }

        lock (_receiveLock)
        {
            Monitor.PulseAll(_receiveLock);
        }

        _ackReceivedEvent.Set();

        if (_ownsUdpClient)
        {
            try
            {
                _udpClient.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error disposing UDP client");
            }
        }

        try
        {
            OnClosed?.Invoke(this);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error invoking OnClosed callback");
        }
    }

    private class InFlightPacket
    {
        public ushort SequenceNumber { get; set; }
        public byte[] PacketData { get; set; }
        public int PayloadLength { get; set; }
        public long SentTimestamp { get; set; }
        public int Retries { get; set; }
        public int SackAckedHigherCount { get; set; }
        public bool FastRetransmitted { get; set; }
    }
}
