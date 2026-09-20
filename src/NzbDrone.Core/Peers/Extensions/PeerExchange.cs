using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Peers.Lpd;

namespace NzbDrone.Core.Peers.Extensions;

public interface IPeerExchange
{
    int IntervalSeconds { get; }
    byte[] BuildPexMessage(List<PeerInfo> added, List<PeerInfo> dropped, bool isPrivate = false);
    PexData ParsePexMessage(byte[] data, bool isPrivate = false);
}

public static class PexFlags
{
    public const byte EncryptionPreferred = 0x01;
    public const byte UploadOnly = 0x02;
    public const byte Seeder = 0x02;
    public const byte UtpSupported = 0x04;
    public const byte HolepunchSupported = 0x08;
}

public class PeerInfo
{
    public const byte FlagEncryptionPreferred = 0x01;
    public const byte FlagUploadOnly = 0x02;
    public const byte FlagSeeder = 0x02;
    public const byte FlagUtpSupported = 0x04;
    public const byte FlagHolepunchSupported = 0x08;

    public string Ip { get; set; }
    public int Port { get; set; }
    public byte Flags { get; set; }
    public bool PrefersEncryption => (Flags & FlagEncryptionPreferred) != 0;
    public bool IsSeeder => (Flags & FlagSeeder) != 0;
    public bool SupportsUtp => (Flags & FlagUtpSupported) != 0;
    public bool SupportsHolepunch => (Flags & FlagHolepunchSupported) != 0;
}

public class PexData
{
    public List<PeerInfo> Added { get; set; } = new();
    public List<PeerInfo> Dropped { get; set; } = new();
    public List<PeerInfo> Added6 { get; set; } = new();
    public List<PeerInfo> Dropped6 { get; set; } = new();
}

public class PeerExchange : IPeerExchange
{
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public int IntervalSeconds => _configService.PexInterval;

    public PeerExchange(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public static bool IsRoutablePublicPeer(IPAddress ip, int port)
    {
        if (ip == null || port <= 0 || port > 65535)
        {
            return false;
        }

        if (IPAddress.IsLoopback(ip))
        {
            return false;
        }

        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast) || ip.Equals(IPAddress.None))
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 0)
            {
                return false;
            }

            if (bytes[0] == 127)
            {
                return false;
            }

            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return false;
            }

            if (bytes[0] >= 224)
            {
                return false;
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    public static List<PeerInfo> ParseCompactPeers(ReadOnlySpan<byte> data, bool isIPv6 = false, int listeningPort = 0)
    {
        return ParseCompactPeers(data, ReadOnlySpan<byte>.Empty, isIPv6, listeningPort);
    }

    public static List<PeerInfo> ParseCompactPeers(ReadOnlySpan<byte> data, ReadOnlySpan<byte> flags, bool isIPv6 = false, int listeningPort = 0)
    {
        var peers = new List<PeerInfo>();
        var stride = isIPv6 ? 18 : 6;
        var ipLen = isIPv6 ? 16 : 4;

        if (data.IsEmpty || data.Length < stride)
        {
            return peers;
        }

        var trailing = data.Length % stride;
        if (trailing != 0)
        {
            data = data.Slice(0, data.Length - trailing);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < data.Length; i += stride)
        {
            var peerIndex = i / stride;
            var ip = new IPAddress(data.Slice(i, ipLen));
            var port = (data[i + ipLen] << 8) | data[i + ipLen + 1];

            if (port <= 0 || port > 65535)
            {
                continue;
            }

            if (!IsRoutablePublicPeer(ip, port))
            {
                continue;
            }

            if (listeningPort > 0 && port == listeningPort && (IPAddress.IsLoopback(ip) || LocalPeerDiscovery.IsLocalAddress(ip)))
            {
                continue;
            }

            var endpoint = isIPv6 ? $"[{ip}]:{port}" : $"{ip}:{port}";
            if (!seen.Add(endpoint))
            {
                continue;
            }

            var flag = peerIndex < flags.Length ? flags[peerIndex] : (byte)0;

            peers.Add(new PeerInfo
            {
                Ip = ip.ToString(),
                Port = port,
                Flags = flag
            });
        }

        return peers;
    }

    public static List<PeerInfo> ParseCompactPeers6(ReadOnlySpan<byte> data, int listeningPort = 0)
    {
        return ParseCompactPeers(data, ReadOnlySpan<byte>.Empty, isIPv6: true, listeningPort);
    }

    public static List<PeerInfo> ParseCompactPeers6(ReadOnlySpan<byte> data, ReadOnlySpan<byte> flags, int listeningPort = 0)
    {
        return ParseCompactPeers(data, flags, isIPv6: true, listeningPort);
    }

    public static byte[] CompactPeers(List<PeerInfo> peers)
    {
        var ipv4Peers = peers
            .Where(p => IPAddress.TryParse(p.Ip, out var addr) && addr.AddressFamily == AddressFamily.InterNetwork)
            .ToList();

        var data = new byte[ipv4Peers.Count * 6];
        for (var i = 0; i < ipv4Peers.Count; i++)
        {
            var addr = IPAddress.Parse(ipv4Peers[i].Ip);
            var ipBytes = addr.GetAddressBytes();
            Buffer.BlockCopy(ipBytes, 0, data, i * 6, 4);
            data[(i * 6) + 4] = (byte)(ipv4Peers[i].Port >> 8);
            data[(i * 6) + 5] = (byte)ipv4Peers[i].Port;
        }

        return data;
    }

    public static byte[] CompactPeers6(List<PeerInfo> peers)
    {
        var ipv6Peers = peers
            .Where(p => IPAddress.TryParse(p.Ip, out var addr) && addr.AddressFamily == AddressFamily.InterNetworkV6)
            .ToList();

        var data = new byte[ipv6Peers.Count * 18];
        for (var i = 0; i < ipv6Peers.Count; i++)
        {
            var addr = IPAddress.Parse(ipv6Peers[i].Ip);
            var ipBytes = addr.GetAddressBytes();
            Buffer.BlockCopy(ipBytes, 0, data, i * 18, 16);
            data[(i * 18) + 16] = (byte)(ipv6Peers[i].Port >> 8);
            data[(i * 18) + 17] = (byte)ipv6Peers[i].Port;
        }

        return data;
    }

    public byte[] BuildPexMessage(List<PeerInfo> added, List<PeerInfo> dropped, bool isPrivate = false)
    {
        if (!_configService.EnablePex || isPrivate)
        {
            return Array.Empty<byte>();
        }

        var uniqueAdded = new List<PeerInfo>();
        var seenAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (added != null)
        {
            foreach (var peer in added)
            {
                if (peer == null || string.IsNullOrWhiteSpace(peer.Ip))
                {
                    continue;
                }

                var key = $"{peer.Ip}:{peer.Port}";
                if (seenAdded.Add(key))
                {
                    uniqueAdded.Add(peer);
                }
            }
        }

        var uniqueDropped = new List<PeerInfo>();
        var seenDropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (dropped != null)
        {
            foreach (var peer in dropped)
            {
                if (peer == null || string.IsNullOrWhiteSpace(peer.Ip))
                {
                    continue;
                }

                var key = $"{peer.Ip}:{peer.Port}";
                if (!seenAdded.Contains(key) && seenDropped.Add(key))
                {
                    uniqueDropped.Add(peer);
                }
            }
        }

        var maxPeers = _configService != null && _configService.PexMaxPeersPerMessage > 0 ? _configService.PexMaxPeersPerMessage : 50;
        var cappedAdded = uniqueAdded.Count > maxPeers ? uniqueAdded.Take(maxPeers).ToList() : uniqueAdded;
        var cappedDropped = uniqueDropped.Count > maxPeers ? uniqueDropped.Take(maxPeers).ToList() : uniqueDropped;

        var ipv4Added = cappedAdded
            .Where(p => IPAddress.TryParse(p.Ip, out var addr) && addr.AddressFamily == AddressFamily.InterNetwork)
            .ToList();
        var addedCompact = CompactPeers(ipv4Added);
        var addedFlags = new byte[ipv4Added.Count];
        for (var i = 0; i < ipv4Added.Count; i++)
        {
            addedFlags[i] = ipv4Added[i].Flags;
        }

        var ipv6Added = cappedAdded
            .Where(p => IPAddress.TryParse(p.Ip, out var addr) && addr.AddressFamily == AddressFamily.InterNetworkV6)
            .ToList();
        var added6Compact = CompactPeers6(ipv6Added);
        var added6Flags = new byte[ipv6Added.Count];
        for (var i = 0; i < ipv6Added.Count; i++)
        {
            added6Flags[i] = ipv6Added[i].Flags;
        }

        var droppedCompact = CompactPeers(cappedDropped);
        var dropped6Compact = CompactPeers6(cappedDropped);

        var dict = new BDictionary
        {
            ["added"] = new BString(addedCompact),
            ["added.f"] = new BString(addedFlags),
            ["dropped"] = new BString(droppedCompact)
        };

        if (added6Compact.Length > 0)
        {
            dict["added6"] = new BString(added6Compact);
            dict["added6.f"] = new BString(added6Flags);
        }

        if (dropped6Compact.Length > 0)
        {
            dict["dropped6"] = new BString(dropped6Compact);
        }

        return dict.EncodeAsBytes();
    }

    public PexData ParsePexMessage(byte[] data, bool isPrivate = false)
    {
        if (!_configService.EnablePex || isPrivate || data == null || data.Length == 0)
        {
            return new PexData();
        }

        try
        {
            var parser = new BencodeParser();
            using var stream = new MemoryStream(data);
            var dict = parser.Parse<BDictionary>(stream);
            var result = new PexData();
            var listeningPort = _configService?.ListeningPort ?? 0;

            ReadOnlySpan<byte> addedFlags = default;
            if (dict.ContainsKey("added.f") && dict["added.f"] is BString addedFlagsBStr)
            {
                addedFlags = addedFlagsBStr.Value.Span;
            }

            ReadOnlySpan<byte> added6Flags = default;
            if (dict.ContainsKey("added6.f") && dict["added6.f"] is BString added6FlagsBStr)
            {
                added6Flags = added6FlagsBStr.Value.Span;
            }

            if (dict.ContainsKey("added"))
            {
                var addedBytes = ((BString)dict["added"]).Value;
                result.Added.AddRange(ParseCompactPeers(addedBytes.Span, addedFlags, isIPv6: false, listeningPort));
            }

            if (dict.ContainsKey("added6"))
            {
                var added6Bytes = ((BString)dict["added6"]).Value;
                var peers6 = ParseCompactPeers(added6Bytes.Span, added6Flags, isIPv6: true, listeningPort);
                result.Added.AddRange(peers6);
                result.Added6.AddRange(peers6);
            }

            if (dict.ContainsKey("dropped"))
            {
                var droppedBytes = ((BString)dict["dropped"]).Value;
                result.Dropped.AddRange(ParseCompactPeers(droppedBytes.Span, isIPv6: false, listeningPort));
            }

            if (dict.ContainsKey("dropped6"))
            {
                var dropped6Bytes = ((BString)dict["dropped6"]).Value;
                var dropped6Peers = ParseCompactPeers(dropped6Bytes.Span, isIPv6: true, listeningPort);
                result.Dropped.AddRange(dropped6Peers);
                result.Dropped6.AddRange(dropped6Peers);
            }

            var seenAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            result.Added = result.Added.Where(p => seenAdded.Add($"{p.Ip}:{p.Port}")).ToList();

            var seenDropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            result.Dropped = result.Dropped.Where(p => seenDropped.Add($"{p.Ip}:{p.Port}")).ToList();

            return result;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse PEX message");
            return new PexData();
        }
    }
}
