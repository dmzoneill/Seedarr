using System.IO;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using NUnit.Framework;
using NzbDrone.Core.Peers.Extensions;

namespace NzbDrone.Core.Test.Peers.Extensions;

[TestFixture]
public class MetadataExchangeTest
{
    private MetadataExchange _exchange;

    [SetUp]
    public void Setup()
    {
        _exchange = new MetadataExchange();
    }

    [Test]
    public void BuildMetadataRequest_should_return_bencoded_data()
    {
        var result = _exchange.BuildMetadataRequest(0);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.GreaterThan(0));
    }

    [Test]
    public void BuildMetadataRequest_should_contain_msg_type_zero()
    {
        var result = _exchange.BuildMetadataRequest(5);
        var dict = ParseBencode(result);

        Assert.That((int)((BNumber)dict["msg_type"]).Value, Is.EqualTo(0));
    }

    [Test]
    public void BuildMetadataRequest_should_contain_piece_number()
    {
        var result = _exchange.BuildMetadataRequest(7);
        var dict = ParseBencode(result);

        Assert.That((int)((BNumber)dict["piece"]).Value, Is.EqualTo(7));
    }

    [Test]
    public void BuildMetadataResponse_should_contain_msg_type_one()
    {
        var data = new byte[] { 0xAA, 0xBB };
        var result = _exchange.BuildMetadataResponse(0, 1024, data);
        var dict = ParseBencode(result);

        Assert.That((int)((BNumber)dict["msg_type"]).Value, Is.EqualTo(1));
    }

    [Test]
    public void BuildMetadataResponse_should_contain_total_size()
    {
        var data = new byte[] { 0x01 };
        var result = _exchange.BuildMetadataResponse(0, 65536, data);
        var dict = ParseBencode(result);

        Assert.That((int)((BNumber)dict["total_size"]).Value, Is.EqualTo(65536));
    }

    [Test]
    public void BuildMetadataResponse_should_append_data_after_header()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var result = _exchange.BuildMetadataResponse(0, 100, data);

        Assert.That(result[result.Length - 4], Is.EqualTo(0xDE));
        Assert.That(result[result.Length - 3], Is.EqualTo(0xAD));
        Assert.That(result[result.Length - 2], Is.EqualTo(0xBE));
        Assert.That(result[result.Length - 1], Is.EqualTo(0xEF));
    }

    [Test]
    public void ParseMetadataMessage_should_parse_request()
    {
        var encoded = _exchange.BuildMetadataRequest(3);

        var result = _exchange.ParseMetadataMessage(encoded);

        Assert.That(result.MessageType, Is.EqualTo(0));
        Assert.That(result.Piece, Is.EqualTo(3));
    }

    [Test]
    public void ParseMetadataMessage_should_parse_response_with_total_size()
    {
        var dict = new BDictionary
        {
            ["msg_type"] = new BNumber(1),
            ["piece"] = new BNumber(2),
            ["total_size"] = new BNumber(32768)
        };
        var encoded = dict.EncodeAsBytes();

        var result = _exchange.ParseMetadataMessage(encoded);

        Assert.That(result.MessageType, Is.EqualTo(1));
        Assert.That(result.Piece, Is.EqualTo(2));
        Assert.That(result.TotalSize, Is.EqualTo(32768));
    }

    [Test]
    public void ParseMetadataMessage_should_return_empty_on_invalid_data()
    {
        var result = _exchange.ParseMetadataMessage(new byte[] { 0xFF, 0x00, 0x01 });

        Assert.That(result.MessageType, Is.EqualTo(0));
        Assert.That(result.Piece, Is.EqualTo(0));
        Assert.That(result.TotalSize, Is.EqualTo(0));
    }

    [Test]
    public void BuildMetadataRequest_roundtrip_should_preserve_piece()
    {
        var encoded = _exchange.BuildMetadataRequest(42);
        var parsed = _exchange.ParseMetadataMessage(encoded);

        Assert.That(parsed.Piece, Is.EqualTo(42));
        Assert.That(parsed.MessageType, Is.EqualTo(0));
    }

    [Test]
    public void BuildMetadataResponse_should_contain_piece_number()
    {
        var data = new byte[] { 0x01 };
        var result = _exchange.BuildMetadataResponse(9, 1024, data);
        var dict = ParseBencode(result);

        Assert.That((int)((BNumber)dict["piece"]).Value, Is.EqualTo(9));
    }

    [Test]
    public void ParseMetadataMessage_should_default_total_size_to_zero_for_request()
    {
        var encoded = _exchange.BuildMetadataRequest(0);
        var result = _exchange.ParseMetadataMessage(encoded);

        Assert.That(result.TotalSize, Is.EqualTo(0));
    }

    [Test]
    public void BuildMetadataResponse_should_have_length_of_header_plus_data()
    {
        var data = new byte[100];
        var headerOnly = new BDictionary
        {
            ["msg_type"] = new BNumber(1),
            ["piece"] = new BNumber(0),
            ["total_size"] = new BNumber(100)
        };
        var headerLen = headerOnly.EncodeAsBytes().Length;

        var result = _exchange.BuildMetadataResponse(0, 100, data);

        Assert.That(result.Length, Is.EqualTo(headerLen + 100));
    }

    [Test]
    public void ParseMetadataMessage_should_extract_piece_data_from_response()
    {
        var rawData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var encoded = _exchange.BuildMetadataResponse(1, 4096, rawData);
        var parsed = _exchange.ParseMetadataMessage(encoded);

        Assert.That(parsed.MessageType, Is.EqualTo(1));
        Assert.That(parsed.Piece, Is.EqualTo(1));
        Assert.That(parsed.TotalSize, Is.EqualTo(4096));
        Assert.That(parsed.Data, Is.Not.Null);
        Assert.That(parsed.Data, Is.EqualTo(rawData));
    }

    private static BDictionary ParseBencode(byte[] data)
    {
        var parser = new BencodeParser();
        using var stream = new MemoryStream(data);
        return parser.Parse<BDictionary>(stream);
    }
}
