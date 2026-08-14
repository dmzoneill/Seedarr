using System;
using System.Collections.Generic;
using System.IO;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;

namespace NzbDrone.Core.Peers.Extensions;

public interface IPeerExchange
{
    byte[] BuildPexMessage(List<PeerInfo> added, List<PeerInfo> dropped);
    PexData ParsePexMessage(byte[] data);
}

public class PeerInfo
{
    public string Ip { get; set; }
    public int Port { get; set; }
}

public class PexData
{
    public List<PeerInfo> Added { get; set; } = new();
    public List<PeerInfo> Dropped { get; set; } = new();
}

public class PeerExchange : IPeerExchange
{
    private readonly Logger _logger;

    public PeerExchange()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public byte[] BuildPexMessage(List<PeerInfo> added, List<PeerInfo> dropped)
    {
        var addedCompact = CompactPeers(added);
        var droppedCompact = CompactPeers(dropped);

        var dict = new BDictionary
        {
            ["added"] = new BString(addedCompact),
            ["dropped"] = new BString(droppedCompact)
        };

        return dict.EncodeAsBytes();
    }

    public PexData ParsePexMessage(byte[] data)
    {
        try
        {
            var parser = new BencodeParser();
            using var stream = new MemoryStream(data);
            var dict = parser.Parse<BDictionary>(stream);
            var result = new PexData();

            if (dict.ContainsKey("added"))
            {
                var addedBytes = ((BString)dict["added"]).Value;
                result.Added = ParseCompactPeers(addedBytes.Span);
            }

            if (dict.ContainsKey("dropped"))
            {
                var droppedBytes = ((BString)dict["dropped"]).Value;
                result.Dropped = ParseCompactPeers(droppedBytes.Span);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse PEX message");
            return new PexData();
        }
    }

    private static byte[] CompactPeers(List<PeerInfo> peers)
    {
        var data = new byte[peers.Count * 6];
        for (var i = 0; i < peers.Count; i++)
        {
            var parts = peers[i].Ip.Split('.');
            data[i * 6] = byte.Parse(parts[0]);
            data[(i * 6) + 1] = byte.Parse(parts[1]);
            data[(i * 6) + 2] = byte.Parse(parts[2]);
            data[(i * 6) + 3] = byte.Parse(parts[3]);
            data[(i * 6) + 4] = (byte)(peers[i].Port >> 8);
            data[(i * 6) + 5] = (byte)peers[i].Port;
        }

        return data;
    }

    private static List<PeerInfo> ParseCompactPeers(ReadOnlySpan<byte> data)
    {
        var peers = new List<PeerInfo>();
        for (var i = 0; i + 5 < data.Length; i += 6)
        {
            peers.Add(new PeerInfo
            {
                Ip = $"{data[i]}.{data[i + 1]}.{data[i + 2]}.{data[i + 3]}",
                Port = (data[i + 4] << 8) | data[i + 5]
            });
        }

        return peers;
    }
}
