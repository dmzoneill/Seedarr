using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace NzbDrone.Core.Blocklist;

public static class IpAddressExtensions
{
    /// <summary>
    /// Converts an IPv6 (or IPv4) address to a 128-bit unsigned integer in big-endian network byte order.
    /// If the address is IPv4, it is mapped to an IPv4-mapped IPv6 address (::ffff:x.x.x.x).
    /// </summary>
    public static UInt128 ToUInt128(this IPAddress ipAddress)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Span<byte> bytes = stackalloc byte[16];
            ipAddress.TryWriteBytes(bytes, out _);
            return BinaryPrimitives.ReadUInt128BigEndian(bytes);
        }

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            return ipAddress.MapToIPv6().ToUInt128();
        }

        throw new ArgumentException($"Unsupported address family: {ipAddress.AddressFamily}", nameof(ipAddress));
    }

    /// <summary>
    /// Converts a 128-bit unsigned integer in big-endian network byte order to an IPv6 IPAddress.
    /// </summary>
    public static IPAddress ToIPv6Address(this UInt128 value)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128BigEndian(bytes, value);
        return new IPAddress(bytes);
    }

    /// <summary>
    /// Converts an IPv4 address (or IPv4-mapped IPv6 address) to a 32-bit unsigned integer in big-endian network byte order.
    /// </summary>
    public static uint ToUInt32(this IPAddress ipAddress)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        if (ipAddress.IsIPv4MappedToIPv6)
        {
            ipAddress = ipAddress.MapToIPv4();
        }

        if (ipAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException($"Cannot convert non-IPv4 address to UInt32: {ipAddress}", nameof(ipAddress));
        }

        Span<byte> bytes = stackalloc byte[4];
        ipAddress.TryWriteBytes(bytes, out _);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    /// <summary>
    /// Converts a 32-bit unsigned integer in big-endian network byte order to an IPv4 IPAddress.
    /// </summary>
    public static IPAddress ToIPv4Address(this uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }
}
