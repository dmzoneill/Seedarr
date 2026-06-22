using System;
using System.Net;
using System.Net.Sockets;
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
        _connectionId = (ushort)new Random().Next(0, ushort.MaxValue);
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

        packet[2] = (byte)(_connectionId >> 8);
        packet[3] = (byte)_connectionId;

        var timestamp = GetMicroseconds();
        packet[4] = (byte)(timestamp >> 24);
        packet[5] = (byte)(timestamp >> 16);
        packet[6] = (byte)(timestamp >> 8);
        packet[7] = (byte)timestamp;

        packet[12] = (byte)(DefaultWindowSize >> 24);
        packet[13] = (byte)(DefaultWindowSize >> 16);
        packet[14] = (byte)(DefaultWindowSize >> 8);
        packet[15] = (byte)(DefaultWindowSize & 0xFF);

        packet[16] = (byte)(_sequenceNumber >> 8);
        packet[17] = (byte)_sequenceNumber;

        packet[18] = (byte)(_ackNumber >> 8);
        packet[19] = (byte)_ackNumber;

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
            ConnectionId = (ushort)((data[2] << 8) | data[3]),
            Timestamp = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]),
            TimestampDiff = (uint)((data[8] << 24) | (data[9] << 16) | (data[10] << 8) | data[11]),
            WindowSize = (uint)((data[12] << 24) | (data[13] << 16) | (data[14] << 8) | data[15]),
            SequenceNumber = (ushort)((data[16] << 8) | data[17]),
            AckNumber = (ushort)((data[18] << 8) | data[19])
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
