using System;
using System.Buffers.Binary;
using System.Net;

namespace NzbDrone.Core.Network.NatPmp;

public enum NatPmpProtocol : byte
{
    Udp = 1,
    Tcp = 2
}

public enum NatPmpResultCode : ushort
{
    Success = 0,
    UnsupportedVersion = 1,
    NotAuthorized = 2,
    NetworkFailure = 3,
    OutOfResources = 4,
    UnsupportedOpcode = 5
}

public class NatPmpException : Exception
{
    public NatPmpResultCode ResultCode { get; }

    public NatPmpException(NatPmpResultCode resultCode, string message)
        : base(message)
    {
        ResultCode = resultCode;
    }

    public NatPmpException(string message)
        : base(message)
    {
    }

    public NatPmpException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public record NatPmpExternalIpResponse(
    byte Version,
    byte Opcode,
    NatPmpResultCode ResultCode,
    uint Epoch,
    IPAddress ExternalAddress);

public record NatPmpMappingResponse(
    byte Version,
    byte Opcode,
    NatPmpResultCode ResultCode,
    uint Epoch,
    ushort InternalPort,
    ushort MappedExternalPort,
    uint Lifetime);

public static class NatPmpPacket
{
    public const int NatPmpPort = 5351;
    public const byte Version = 0;
    public const byte OpcodeExternalIp = 0;
    public const byte OpcodeMapUdp = 1;
    public const byte OpcodeMapTcp = 2;

    public const byte OpcodeExternalIpResponse = 128;
    public const byte OpcodeMapUdpResponse = 129;
    public const byte OpcodeMapTcpResponse = 130;

    public const int ExternalIpRequestLength = 2;
    public const int ExternalIpResponseLength = 12;
    public const int PortMappingRequestLength = 12;
    public const int PortMappingResponseLength = 16;

    public static byte[] CreateExternalIpRequest()
    {
        return new byte[] { Version, OpcodeExternalIp };
    }

    public static byte[] CreatePortMappingRequest(
        NatPmpProtocol protocol,
        ushort internalPort,
        ushort suggestedExternalPort,
        uint lifetimeSeconds)
    {
        var buffer = new byte[PortMappingRequestLength];
        buffer[0] = Version;
        buffer[1] = (byte)protocol;
        buffer[2] = 0;
        buffer[3] = 0;

        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4, 2), internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6, 2), suggestedExternalPort);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8, 4), lifetimeSeconds);

        return buffer;
    }

    public static byte[] CreateDeletePortMappingRequest(NatPmpProtocol protocol, ushort internalPort)
    {
        return CreatePortMappingRequest(protocol, internalPort, suggestedExternalPort: 0, lifetimeSeconds: 0);
    }

    public static NatPmpExternalIpResponse ParseExternalIpResponse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 8)
        {
            throw new NatPmpException("External IP response packet is too short (minimum 8 bytes required).");
        }

        var version = buffer[0];
        var opcode = buffer[1];
        if (opcode != OpcodeExternalIpResponse)
        {
            throw new NatPmpException($"Invalid opcode in External IP response: {opcode}, expected {OpcodeExternalIpResponse}.");
        }

        var resultCode = (NatPmpResultCode)BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2));
        var epoch = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));

        if (resultCode != NatPmpResultCode.Success)
        {
            return new NatPmpExternalIpResponse(version, opcode, resultCode, epoch, IPAddress.None);
        }

        if (buffer.Length < ExternalIpResponseLength)
        {
            throw new NatPmpException($"Successful External IP response must be at least {ExternalIpResponseLength} bytes.");
        }

        var ipBytes = buffer.Slice(8, 4).ToArray();
        var externalIp = new IPAddress(ipBytes);

        return new NatPmpExternalIpResponse(version, opcode, resultCode, epoch, externalIp);
    }

    public static NatPmpMappingResponse ParsePortMappingResponse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 8)
        {
            throw new NatPmpException("Port mapping response packet is too short (minimum 8 bytes required).");
        }

        var version = buffer[0];
        var opcode = buffer[1];
        if (opcode != OpcodeMapUdpResponse && opcode != OpcodeMapTcpResponse)
        {
            throw new NatPmpException($"Invalid opcode in Port Mapping response: {opcode}, expected {OpcodeMapUdpResponse} or {OpcodeMapTcpResponse}.");
        }

        var resultCode = (NatPmpResultCode)BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2));
        var epoch = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));

        if (resultCode != NatPmpResultCode.Success)
        {
            return new NatPmpMappingResponse(version, opcode, resultCode, epoch, 0, 0, 0);
        }

        if (buffer.Length < PortMappingResponseLength)
        {
            throw new NatPmpException($"Successful Port Mapping response must be at least {PortMappingResponseLength} bytes.");
        }

        var internalPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(8, 2));
        var mappedExternalPort = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(10, 2));
        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(12, 4));

        return new NatPmpMappingResponse(version, opcode, resultCode, epoch, internalPort, mappedExternalPort, lifetime);
    }

    public static string GetResultCodeMessage(NatPmpResultCode code) => code switch
    {
        NatPmpResultCode.Success => "Success",
        NatPmpResultCode.UnsupportedVersion => "Unsupported version",
        NatPmpResultCode.NotAuthorized => "Not authorized / Refused",
        NatPmpResultCode.NetworkFailure => "Network failure",
        NatPmpResultCode.OutOfResources => "Out of resources",
        NatPmpResultCode.UnsupportedOpcode => "Unsupported opcode",
        _ => $"Unknown NAT-PMP result code: {(ushort)code}"
    };
}
