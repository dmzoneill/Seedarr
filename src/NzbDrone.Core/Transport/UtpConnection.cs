using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    void Connect(IPEndPoint endpoint);
    int Send(byte[] data, int offset, int length);
    int Receive(byte[] buffer, int offset, int length);
}

public class UtpConnection : IUtpConnection
{
    private const int HeaderSize = 20;
    private const uint DefaultWindowSize = 65535;

    private readonly UdpClient _udpClient;
    private readonly Logger _logger;
    private readonly int _connectionTimeoutSeconds;
    private ushort _connectionId;
    private ushort _sequenceNumber;
    private ushort _ackNumber;
    private IPEndPoint _remoteEndpoint;

    public bool IsConnected { get; private set; }

    public UtpConnection(int connectionTimeoutSeconds = 30)
    {
        _udpClient = new UdpClient();
        _logger = LogManager.GetCurrentClassLogger();
        _connectionId = (ushort)RandomNumberGenerator.GetInt32(0, ushort.MaxValue + 1);
        _sequenceNumber = 1;
        _connectionTimeoutSeconds = connectionTimeoutSeconds;
    }

    public void Connect(IPEndPoint endpoint)
    {
        _remoteEndpoint = endpoint;
        _udpClient.Client.ReceiveTimeout = _connectionTimeoutSeconds * 1000;
        _udpClient.Client.SendTimeout = _connectionTimeoutSeconds * 1000;

        var synPacket = BuildPacket(UtpPacketType.Syn, Array.Empty<byte>());
        _udpClient.Send(synPacket, synPacket.Length, _remoteEndpoint);

        var receiveEndpoint = new IPEndPoint(IPAddress.Any, 0);
        var response = _udpClient.Receive(ref receiveEndpoint);

        if (response.Length >= HeaderSize)
        {
            var header = ParseHeader(response);
            if (header.Type == UtpPacketType.State)
            {
                _ackNumber = header.SequenceNumber;
                _connectionId = (ushort)(header.ConnectionId + 1);
                IsConnected = true;
                _logger.Debug("uTP connected to {0}", endpoint);
            }
        }
    }

    public int Send(byte[] data, int offset, int length)
    {
        if (!IsConnected)
        {
            return 0;
        }

        var payload = new byte[length];
        Array.Copy(data, offset, payload, 0, length);

        var packet = BuildPacket(UtpPacketType.Data, payload);
        _udpClient.Send(packet, packet.Length, _remoteEndpoint);
        _sequenceNumber++;

        return length;
    }

    public int Receive(byte[] buffer, int offset, int length)
    {
        if (!IsConnected)
        {
            return 0;
        }

        var receiveEndpoint = new IPEndPoint(IPAddress.Any, 0);
        var data = _udpClient.Receive(ref receiveEndpoint);

        if (data.Length <= HeaderSize)
        {
            return 0;
        }

        var header = ParseHeader(data);
        _ackNumber = header.SequenceNumber;

        var ack = BuildPacket(UtpPacketType.State, Array.Empty<byte>());
        _udpClient.Send(ack, ack.Length, _remoteEndpoint);

        var payloadLength = Math.Min(data.Length - HeaderSize, length);
        Array.Copy(data, HeaderSize, buffer, offset, payloadLength);

        return payloadLength;
    }

    private byte[] BuildPacket(UtpPacketType type, byte[] payload)
    {
        var packet = new byte[HeaderSize + payload.Length];

        packet[0] = (byte)(((byte)type << 4) | 1);
        packet[1] = 0;

        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), _connectionId);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), GetMicroseconds());
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12, 4), DefaultWindowSize);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(16, 2), _sequenceNumber);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2), _ackNumber);

        if (payload.Length > 0)
        {
            Array.Copy(payload, 0, packet, HeaderSize, payload.Length);
        }

        return packet;
    }

    private static UtpHeader ParseHeader(byte[] data)
    {
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
        return (uint)(Environment.TickCount64 * 1000 & 0xFFFFFFFF);
    }

    public void Dispose()
    {
        if (IsConnected)
        {
            try
            {
                var fin = BuildPacket(UtpPacketType.Fin, Array.Empty<byte>());
                _udpClient.Send(fin, fin.Length, _remoteEndpoint);
            }
            catch
            {
                // Best effort
            }

            IsConnected = false;
        }

        _udpClient.Dispose();
    }
}
