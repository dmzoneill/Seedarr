using System;
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
    public int MessageType { get; set; }  // 0=request, 1=data, 2=reject
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
        var dict = new BencodeNET.Objects.BDictionary
        {
            ["msg_type"] = new BencodeNET.Objects.BNumber(0),
            ["piece"] = new BencodeNET.Objects.BNumber(piece)
        };

        return dict.EncodeAsBytes();
    }

    public byte[] BuildMetadataResponse(int piece, int totalSize, byte[] data)
    {
        var dict = new BencodeNET.Objects.BDictionary
        {
            ["msg_type"] = new BencodeNET.Objects.BNumber(1),
            ["piece"] = new BencodeNET.Objects.BNumber(piece),
            ["total_size"] = new BencodeNET.Objects.BNumber(totalSize)
        };

        var header = dict.EncodeAsBytes();
        var result = new byte[header.Length + data.Length];
        Array.Copy(header, 0, result, 0, header.Length);
        Array.Copy(data, 0, result, header.Length, data.Length);
        return result;
    }

    public MetadataMessage ParseMetadataMessage(byte[] data)
    {
        try
        {
            var parser = new BencodeNET.Parsing.BencodeParser();
            var dict = parser.Parse<BencodeNET.Objects.BDictionary>(data);

            return new MetadataMessage
            {
                MessageType = (int)((BencodeNET.Objects.BNumber)dict["msg_type"]).Value,
                Piece = (int)((BencodeNET.Objects.BNumber)dict["piece"]).Value,
                TotalSize = dict.ContainsKey("total_size") ? (int)((BencodeNET.Objects.BNumber)dict["total_size"]).Value : 0
            };
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse metadata message");
            return new MetadataMessage();
        }
    }
}
