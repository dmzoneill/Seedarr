using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace NzbDrone.Core.Network.Pcp;

public enum PcpResultCode : byte
{
    Success = 0,
    UnsupportedVersion = 1,
    NotAuthorized = 2,
    MalformedRequest = 3,
    UnsupportedOpcode = 4,
    UnsupportedOption = 5,
    MalformedOption = 6,
    NetworkFailure = 7,
    NoResources = 8,
    UnsupportedProtocol = 9,
    UserExceededQuota = 10,
    CannotProvideExternal = 11,
    AddressMismatch = 12,
    ExcessiveRemotePeers = 13
}

public class PcpException : Exception
{
    public PcpResultCode? ResultCode { get; }

    public PcpException(string message)
        : base(message)
    {
    }

    public PcpException(PcpResultCode resultCode, string message)
        : base(message)
    {
        ResultCode = resultCode;
    }

    public PcpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public record PcpResponse(
    byte Version,
    byte Opcode,
    PcpResultCode ResultCode,
    uint LifetimeSeconds,
    uint Epoch,
    byte[] Nonce,
    byte Protocol,
    ushort InternalPort,
    ushort AssignedExternalPort,
    IPAddress AssignedExternalAddress);

public static class PcpPacket
{
    public const int PcpPort = 5351;
    public const byte Version = 2;
    public const byte OpcodeMap = 1;
    public const byte OpcodeMapResponse = 0x81;

    public const int HeaderLength = 24;
    public const int MapPayloadLength = 36;
    public const int TotalPacketLength = HeaderLength + MapPayloadLength;
    public const int NonceLength = 12;

    public static byte[] GenerateNonce()
    {
        return RandomNumberGenerator.GetBytes(NonceLength);
    }

    public static byte[] ConvertIpTo16Bytes(IPAddress ip)
    {
        if (ip == null || ip.Equals(IPAddress.None))
        {
            return new byte[16];
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.GetAddressBytes();
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return ip.MapToIPv6().GetAddressBytes();
        }

        throw new ArgumentException($"Unsupported address family: {ip.AddressFamily}", nameof(ip));
    }

    public static IPAddress Convert16BytesToIp(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16)
        {
            throw new ArgumentException("16 bytes required to construct IPAddress.", nameof(bytes));
        }

        var ip = new IPAddress(bytes);
        if (ip.IsIPv4MappedToIPv6)
        {
            return ip.MapToIPv4();
        }

        return ip;
    }

    public static byte[] CreateMapRequest(
        IPAddress clientIp,
        int internalPort,
        int suggestedExternalPort,
        PortMappingTransport protocol,
        uint lifetimeSeconds,
        byte[] nonce = null,
        IPAddress suggestedExternalIp = null)
    {
        if (clientIp == null)
        {
            throw new ArgumentNullException(nameof(clientIp));
        }

        if (internalPort < 0 || internalPort > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(internalPort), "Internal port must be between 0 and 65535.");
        }

        if (suggestedExternalPort < 0 || suggestedExternalPort > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(suggestedExternalPort), "External port must be between 0 and 65535.");
        }

        if (nonce != null && nonce.Length != NonceLength)
        {
            throw new ArgumentException($"Mapping nonce must be exactly {NonceLength} bytes.", nameof(nonce));
        }

        var buffer = new byte[TotalPacketLength];

        buffer[0] = Version;
        buffer[1] = OpcodeMap;
        buffer[2] = 0;
        buffer[3] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4, 4), lifetimeSeconds);

        var clientIpBytes = ConvertIpTo16Bytes(clientIp);
        clientIpBytes.CopyTo(buffer, 8);

        var actualNonce = nonce ?? GenerateNonce();
        actualNonce.CopyTo(buffer, 24);

        buffer[36] = (byte)protocol;
        buffer[37] = 0;
        buffer[38] = 0;
        buffer[39] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(40, 2), (ushort)internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(42, 2), (ushort)suggestedExternalPort);

        var externalIpBytes = ConvertIpTo16Bytes(suggestedExternalIp ?? IPAddress.IPv6Any);
        externalIpBytes.CopyTo(buffer, 44);

        return buffer;
    }

    public static byte[] CreateDeleteMapRequest(
        IPAddress clientIp,
        int internalPort,
        PortMappingTransport protocol,
        byte[] nonce)
    {
        if (nonce == null || nonce.Length != NonceLength)
        {
            throw new ArgumentException($"Mapping nonce must be exactly {NonceLength} bytes.", nameof(nonce));
        }

        return CreateMapRequest(
            clientIp: clientIp,
            internalPort: internalPort,
            suggestedExternalPort: 0,
            protocol: protocol,
            lifetimeSeconds: 0,
            nonce: nonce,
            suggestedExternalIp: IPAddress.IPv6Any);
    }

    public static PcpResponse ParseMapResponse(ReadOnlySpan<byte> buffer, byte[] expectedNonce = null)
    {
        if (buffer.Length < HeaderLength)
        {
            if (buffer.Length >= 4 && buffer[0] == 0)
            {
                var natPmpResult = (PcpResultCode)BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2));
                var natPmpEpoch = buffer.Length >= 8 ? BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4)) : 0;

                return new PcpResponse(
                    Version: 0,
                    Opcode: buffer[1],
                    ResultCode: natPmpResult,
                    LifetimeSeconds: 0,
                    Epoch: natPmpEpoch,
                    Nonce: expectedNonce ?? Array.Empty<byte>(),
                    Protocol: 0,
                    InternalPort: 0,
                    AssignedExternalPort: 0,
                    AssignedExternalAddress: IPAddress.IPv6Any);
            }

            throw new PcpException($"PCP response packet is too short ({buffer.Length} bytes received, minimum {HeaderLength} bytes required).");
        }

        if (buffer[0] == 0)
        {
            var natPmpResult = (PcpResultCode)BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2));
            var natPmpEpoch = buffer.Length >= 8 ? BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4)) : 0;

            return new PcpResponse(
                Version: 0,
                Opcode: buffer[1],
                ResultCode: natPmpResult,
                LifetimeSeconds: 0,
                Epoch: natPmpEpoch,
                Nonce: expectedNonce ?? Array.Empty<byte>(),
                Protocol: 0,
                InternalPort: 0,
                AssignedExternalPort: 0,
                AssignedExternalAddress: IPAddress.IPv6Any);
        }

        var version = buffer[0];
        var opcode = buffer[1];
        if (opcode != OpcodeMapResponse && (opcode & 0x7F) != OpcodeMap)
        {
            throw new PcpException($"Invalid opcode in PCP response: {opcode}, expected {OpcodeMapResponse}.");
        }

        var resultCode = (PcpResultCode)buffer[3];
        var lifetimeSeconds = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));
        var epoch = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(8, 4));

        var nonce = Array.Empty<byte>();
        if (buffer.Length >= HeaderLength + NonceLength)
        {
            nonce = buffer.Slice(24, NonceLength).ToArray();
            if (expectedNonce != null && !nonce.AsSpan().SequenceEqual(expectedNonce))
            {
                throw new PcpException("Mapping nonce mismatch - potential spoofed response.");
            }
        }
        else if (expectedNonce != null && resultCode == PcpResultCode.Success)
        {
            throw new PcpException("Response packet is too short to contain expected mapping nonce.");
        }

        byte protocol = 0;
        ushort internalPort = 0;
        ushort assignedExternalPort = 0;
        var assignedAddress = IPAddress.IPv6Any;

        if (buffer.Length >= TotalPacketLength)
        {
            protocol = buffer[36];
            internalPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(40, 2));
            assignedExternalPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(42, 2));
            assignedAddress = Convert16BytesToIp(buffer.Slice(44, 16));
        }

        return new PcpResponse(
            Version: version,
            Opcode: opcode,
            ResultCode: resultCode,
            LifetimeSeconds: lifetimeSeconds,
            Epoch: epoch,
            Nonce: nonce,
            Protocol: protocol,
            InternalPort: internalPort,
            AssignedExternalPort: assignedExternalPort,
            AssignedExternalAddress: assignedAddress);
    }

    public static string GetResultCodeMessage(PcpResultCode resultCode)
    {
        return resultCode switch
        {
            PcpResultCode.Success => "Success",
            PcpResultCode.UnsupportedVersion => "Unsupported PCP version",
            PcpResultCode.NotAuthorized => "Not authorized",
            PcpResultCode.MalformedRequest => "Malformed request",
            PcpResultCode.UnsupportedOpcode => "Unsupported opcode",
            PcpResultCode.UnsupportedOption => "Unsupported option",
            PcpResultCode.MalformedOption => "Malformed option",
            PcpResultCode.NetworkFailure => "Network failure",
            PcpResultCode.NoResources => "No resources",
            PcpResultCode.UnsupportedProtocol => "Unsupported protocol",
            PcpResultCode.UserExceededQuota => "User exceeded quota",
            PcpResultCode.CannotProvideExternal => "Cannot provide suggested external port/IP",
            PcpResultCode.AddressMismatch => "Address mismatch",
            PcpResultCode.ExcessiveRemotePeers => "Excessive remote peers",
            _ => $"Unknown PCP result code: {(byte)resultCode}"
        };
    }
}
