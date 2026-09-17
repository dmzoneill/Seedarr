using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace NzbDrone.Core.Dht;

public static class DhtSecurity
{
    private static readonly byte[] V4Mask = { 0x03, 0x0f, 0x3f, 0xff };
    private static readonly byte[] V6Mask = { 0x01, 0x03, 0x07, 0x0f, 0x1f, 0x3f, 0x7f, 0xff };
    private static readonly uint[] Crc32CTable = InitializeTable();

    public static uint ComputeCrc32c(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc = (crc >> 8) ^ Crc32CTable[(crc ^ b) & 0xFF];
        }

        return crc ^ 0xFFFFFFFF;
    }

    public static uint ComputeCrc32c(IPAddress address, byte r)
    {
        ArgumentNullException.ThrowIfNull(address);

        var rBits = (byte)(r & 0x07);

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var ip = address.GetAddressBytes();
            ip[0] = (byte)((ip[0] & V4Mask[0]) | (rBits << 5));
            ip[1] = (byte)(ip[1] & V4Mask[1]);
            ip[2] = (byte)(ip[2] & V4Mask[2]);
            ip[3] = (byte)(ip[3] & V4Mask[3]);

            return ComputeCrc32c(ip);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var ipBytes = address.GetAddressBytes();
            var ip = new byte[8];
            for (var i = 0; i < 8; i++)
            {
                ip[i] = (byte)(ipBytes[i] & V6Mask[i]);
            }

            ip[0] = (byte)(ip[0] | (rBits << 5));

            return ComputeCrc32c(ip);
        }

        throw new ArgumentException($"Unsupported address family: {address.AddressFamily}", nameof(address));
    }

    public static byte[] GenerateNodeId(IPAddress address, byte? r = null)
    {
        ArgumentNullException.ThrowIfNull(address);

        var rand = r ?? RandomNumberGenerator.GetBytes(1)[0];
        var crc = ComputeCrc32c(address, rand);

        var nodeId = new byte[20];
        RandomNumberGenerator.Fill(nodeId);

        nodeId[0] = (byte)((crc >> 24) & 0xFF);
        nodeId[1] = (byte)((crc >> 16) & 0xFF);
        var crcByte2 = (byte)((crc >> 8) & 0xF8);
        var randBits = (byte)(nodeId[2] & 0x07);
        nodeId[2] = (byte)(crcByte2 | randBits);
        nodeId[19] = rand;

        return nodeId;
    }

    public static bool IsNodeIdValid(byte[] nodeId, IPAddress address)
    {
        if (nodeId == null || nodeId.Length != 20 || address == null)
        {
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork &&
            address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        var rand = nodeId[19];
        uint crc;
        try
        {
            crc = ComputeCrc32c(address, rand);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var exp0 = (byte)((crc >> 24) & 0xFF);
        var exp1 = (byte)((crc >> 16) & 0xFF);
        var exp2High5 = (byte)((crc >> 8) & 0xF8);

        return nodeId[0] == exp0 &&
               nodeId[1] == exp1 &&
               (nodeId[2] & 0xF8) == exp2High5;
    }

    private static uint[] InitializeTable()
    {
        const uint polynomial = 0x82F63B78;
        var table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            var entry = i;
            for (var j = 0; j < 8; j++)
            {
                if ((entry & 1) == 1)
                {
                    entry = (entry >> 1) ^ polynomial;
                }
                else
                {
                    entry >>= 1;
                }
            }

            table[i] = entry;
        }

        return table;
    }
}
