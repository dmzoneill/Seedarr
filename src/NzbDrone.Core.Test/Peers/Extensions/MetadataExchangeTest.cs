using System;
using System.IO;
using System.Security.Cryptography;
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

    [Test]
    public void ParseMetadataMessage_should_reject_when_total_size_exceeds_max_metadata_size()
    {
        var dict = new BDictionary
        {
            ["msg_type"] = new BNumber(1),
            ["piece"] = new BNumber(0),
            ["total_size"] = new BNumber(MetadataExchange.MaxMetadataSize + 1)
        };
        var encoded = dict.EncodeAsBytes();

        var result = _exchange.ParseMetadataMessage(encoded);

        Assert.That(result.MessageType, Is.EqualTo(2));
        Assert.That(result.Data, Is.Null);
    }

    [Test]
    public void ParseMetadataMessage_should_reject_when_total_size_is_negative()
    {
        var dict = new BDictionary
        {
            ["msg_type"] = new BNumber(1),
            ["piece"] = new BNumber(0),
            ["total_size"] = new BNumber(-1)
        };
        var encoded = dict.EncodeAsBytes();

        var result = _exchange.ParseMetadataMessage(encoded);

        Assert.That(result.MessageType, Is.EqualTo(2));
        Assert.That(result.Data, Is.Null);
    }

    [Test]
    public void ParseMetadataMessage_should_reject_when_response_has_zero_total_size()
    {
        var dict = new BDictionary
        {
            ["msg_type"] = new BNumber(1),
            ["piece"] = new BNumber(0),
            ["total_size"] = new BNumber(0)
        };
        var encoded = dict.EncodeAsBytes();

        var result = _exchange.ParseMetadataMessage(encoded);

        Assert.That(result.MessageType, Is.EqualTo(2));
        Assert.That(result.Data, Is.Null);
    }

    [Test]
    public void ParseMetadataMessage_should_reject_when_chunk_size_exceeds_max_block_size()
    {
        var oversizedChunk = new byte[MetadataExchange.MetadataBlockSize + 1];
        var encoded = _exchange.BuildMetadataResponse(0, 32768, oversizedChunk);

        var result = _exchange.ParseMetadataMessage(encoded);

        Assert.That(result.MessageType, Is.EqualTo(2));
        Assert.That(result.Data, Is.Null);
    }

    [Test]
    public void ParseMetadataMessage_should_reject_when_final_chunk_size_exceeds_expected_size()
    {
        var invalidFinalChunk = new byte[4000];
        var encoded = _exchange.BuildMetadataResponse(1, 20000, invalidFinalChunk);

        var result = _exchange.ParseMetadataMessage(encoded);

        Assert.That(result.MessageType, Is.EqualTo(2));
        Assert.That(result.Data, Is.Null);
    }

    [Test]
    public void ParseMetadataMessage_should_accept_valid_final_chunk_size()
    {
        var validFinalChunk = new byte[3616];
        var encoded = _exchange.BuildMetadataResponse(1, 20000, validFinalChunk);

        var result = _exchange.ParseMetadataMessage(encoded);

        Assert.That(result.MessageType, Is.EqualTo(1));
        Assert.That(result.Piece, Is.EqualTo(1));
        Assert.That(result.TotalSize, Is.EqualTo(20000));
        Assert.That(result.Data, Is.Not.Null);
        Assert.That(result.Data.Length, Is.EqualTo(3616));
    }

    [Test]
    public void BuildMetadataReject_should_contain_msg_type_two_and_piece()
    {
        var result = _exchange.BuildMetadataReject(3);
        var dict = ParseBencode(result);

        Assert.That((int)((BNumber)dict["msg_type"]).Value, Is.EqualTo(2));
        Assert.That((int)((BNumber)dict["piece"]).Value, Is.EqualTo(3));
    }

    [Test]
    public void ValidateMetadata_should_accept_when_hash_matches()
    {
        var metadataBytes = new byte[1024];
        new Random(42).NextBytes(metadataBytes);
        var expectedHash = Convert.ToHexString(SHA1.HashData(metadataBytes));

        var isValidUpper = _exchange.ValidateMetadata(metadataBytes, expectedHash.ToUpperInvariant());
        var isValidLower = _exchange.ValidateMetadata(metadataBytes, expectedHash.ToLowerInvariant());

        Assert.That(isValidUpper, Is.True);
        Assert.That(isValidLower, Is.True);
    }

    [Test]
    public void ValidateMetadata_should_reject_when_hash_mismatches()
    {
        var metadataBytes = new byte[1024];
        new Random(42).NextBytes(metadataBytes);
        const string mismatchedHash = "0123456789ABCDEF0123456789ABCDEF01234567";

        var isValid = _exchange.ValidateMetadata(metadataBytes, mismatchedHash);

        Assert.That(isValid, Is.False);
    }

    [Test]
    public void ValidateMetadata_should_reject_when_tampered()
    {
        var metadataBytes = new byte[1024];
        new Random(42).NextBytes(metadataBytes);
        var expectedHash = Convert.ToHexString(SHA1.HashData(metadataBytes));

        metadataBytes[0] ^= 0xFF;

        var isValid = _exchange.ValidateMetadata(metadataBytes, expectedHash);

        Assert.That(isValid, Is.False);
    }

    [Test]
    public void ReassembleMetadata_should_reassemble_valid_pieces_matching_infohash()
    {
        const int totalSize = 20000;
        var piece0 = new byte[MetadataExchange.MetadataBlockSize];
        var piece1 = new byte[totalSize - MetadataExchange.MetadataBlockSize];
        new Random(123).NextBytes(piece0);
        new Random(456).NextBytes(piece1);

        var expectedMetadata = new byte[totalSize];
        Array.Copy(piece0, 0, expectedMetadata, 0, piece0.Length);
        Array.Copy(piece1, 0, expectedMetadata, piece0.Length, piece1.Length);

        var expectedHash = Convert.ToHexString(SHA1.HashData(expectedMetadata));
        var pieces = new[] { piece0, piece1 };

        var reassembled = _exchange.ReassembleMetadata(pieces, totalSize, expectedHash);

        Assert.That(reassembled, Is.Not.Null);
        Assert.That(reassembled.Length, Is.EqualTo(totalSize));
        Assert.That(reassembled, Is.EqualTo(expectedMetadata));
    }

    [Test]
    public void ReassembleMetadata_should_discard_and_return_null_when_hash_mismatches()
    {
        const int totalSize = 20000;
        var piece0 = new byte[MetadataExchange.MetadataBlockSize];
        var piece1 = new byte[totalSize - MetadataExchange.MetadataBlockSize];
        new Random(123).NextBytes(piece0);
        new Random(456).NextBytes(piece1);

        const string wrongHash = "0000000000000000000000000000000000000000";
        var pieces = new[] { piece0, piece1 };

        var reassembled = _exchange.ReassembleMetadata(pieces, totalSize, wrongHash);

        Assert.That(reassembled, Is.Null);
    }

    [Test]
    public void ReassembleMetadata_should_discard_and_return_null_when_total_size_invalid()
    {
        var pieces = new[] { new byte[100] };

        Assert.That(_exchange.ReassembleMetadata(pieces, -1, "hash"), Is.Null);
        Assert.That(_exchange.ReassembleMetadata(pieces, MetadataExchange.MaxMetadataSize + 1, "hash"), Is.Null);
    }

    [Test]
    public void ReassembleMetadata_should_discard_and_return_null_when_chunk_size_invalid()
    {
        const int totalSize = 20000;
        var piece0 = new byte[10000];
        var piece1 = new byte[totalSize - 10000];
        var pieces = new[] { piece0, piece1 };

        var reassembled = _exchange.ReassembleMetadata(pieces, totalSize, "hash");

        Assert.That(reassembled, Is.Null);
    }

    private static BDictionary ParseBencode(byte[] data)
    {
        var parser = new BencodeParser();
        using var stream = new MemoryStream(data);
        return parser.Parse<BDictionary>(stream);
    }
}
