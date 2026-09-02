using System;
using System.IO;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NLog;

namespace NzbDrone.Core.Peers.Extensions;

public interface IMetadataExchange
{
    byte[] BuildMetadataRequest(int piece);
    byte[] BuildMetadataResponse(int piece, int totalSize, byte[] data);
    MetadataMessage ParseMetadataMessage(byte[] data);
}

public class MetadataMessage
{
    public int MessageType { get; set; }
    public int Piece { get; set; }
    public int TotalSize { get; set; }
    public byte[] Data { get; set; }
}

public class MetadataExchange : IMetadataExchange
{
    private readonly Logger _logger;

    public MetadataExchange()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public byte[] BuildMetadataRequest(int piece)
    {
        var dict = new BDictionary
        {
            ["msg_type"] = new BNumber(0),
            ["piece"] = new BNumber(piece)
        };

        return dict.EncodeAsBytes();
    }

    public byte[] BuildMetadataResponse(int piece, int totalSize, byte[] data)
    {
        var dict = new BDictionary
        {
            ["msg_type"] = new BNumber(1),
            ["piece"] = new BNumber(piece),
            ["total_size"] = new BNumber(totalSize)
        };

        var header = dict.EncodeAsBytes();
        var result = new byte[header.Length + data.Length];
        Array.Copy(header, 0, result, 0, header.Length);
        Array.Copy(data, 0, result, header.Length, data.Length);
        return result;
    }

    public MetadataMessage ParseMetadataMessage(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return new MetadataMessage();
        }

        try
        {
            var parser = new BencodeParser();
            using var stream = new MemoryStream(data);
            var dict = parser.Parse<BDictionary>(stream);

            byte[] pieceData = null;
            if (stream.Position < stream.Length)
            {
                var remaining = (int)(stream.Length - stream.Position);
                pieceData = new byte[remaining];
                stream.ReadExactly(pieceData, 0, remaining);
            }

            var messageType = dict.TryGetValue("msg_type", out var msgTypeObj) && msgTypeObj is BNumber msgTypeNum
                ? (int)msgTypeNum.Value
                : 0;

            var piece = dict.TryGetValue("piece", out var pieceObj) && pieceObj is BNumber pieceNum
                ? (int)pieceNum.Value
                : 0;

            var totalSize = dict.TryGetValue("total_size", out var totalSizeObj) && totalSizeObj is BNumber totalSizeNum
                ? (int)totalSizeNum.Value
                : 0;

            return new MetadataMessage
            {
                MessageType = messageType,
                Piece = piece,
                TotalSize = totalSize,
                Data = pieceData
            };
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse metadata message");
            return new MetadataMessage();
        }
    }
}
