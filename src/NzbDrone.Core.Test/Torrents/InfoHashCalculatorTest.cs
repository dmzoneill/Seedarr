using System;
using System.Security.Cryptography;
using BencodeNET.Objects;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class InfoHashCalculatorTest
{
    [Test]
    public void Calculate_should_return_sha1_hex_string_for_known_input()
    {
        // A simple bencode dictionary: d4:name5:helloe
        var info = new BDictionary
        {
            { "name", new BString("hello") }
        };

        var result = InfoHashCalculator.Calculate(info);

        // Independently compute expected SHA1 of the bencoded bytes
        var encoded = info.EncodeAsBytes();
        var expectedHash = SHA1.HashData(encoded);
        var expected = Convert.ToHexString(expectedHash).ToLowerInvariant();

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Calculate_should_return_lowercase_hex_string()
    {
        var info = new BDictionary
        {
            { "key", new BString("value") }
        };

        var result = InfoHashCalculator.Calculate(info);

        Assert.That(result, Is.EqualTo(result.ToLowerInvariant()));
    }

    [Test]
    public void Calculate_should_return_40_character_hex_string()
    {
        var info = new BDictionary
        {
            { "name", new BString("test") }
        };

        var result = InfoHashCalculator.Calculate(info);

        // SHA1 produces 20 bytes = 40 hex characters
        Assert.That(result, Has.Length.EqualTo(40));
    }

    [Test]
    public void Calculate_should_produce_different_hashes_for_different_inputs()
    {
        var info1 = new BDictionary
        {
            { "name", new BString("torrent_a") }
        };

        var info2 = new BDictionary
        {
            { "name", new BString("torrent_b") }
        };

        var hash1 = InfoHashCalculator.Calculate(info1);
        var hash2 = InfoHashCalculator.Calculate(info2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Calculate_should_produce_same_hash_for_same_input()
    {
        var info = new BDictionary
        {
            { "name", new BString("consistent") },
            { "piece length", new BNumber(262144) }
        };

        var hash1 = InfoHashCalculator.Calculate(info);
        var hash2 = InfoHashCalculator.Calculate(info);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_should_handle_empty_dictionary()
    {
        var info = new BDictionary();

        var result = InfoHashCalculator.Calculate(info);

        // Empty dict "de" should still produce a valid 40-char SHA1
        Assert.That(result, Has.Length.EqualTo(40));

        // Verify against known SHA1 of "de" (bencoded empty dict)
        var encoded = info.EncodeAsBytes();
        var expectedHash = SHA1.HashData(encoded);
        var expected = Convert.ToHexString(expectedHash).ToLowerInvariant();
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Calculate_should_handle_complex_dictionary()
    {
        var info = new BDictionary
        {
            { "name", new BString("My.Torrent.S01E01.720p") },
            { "piece length", new BNumber(524288) },
            { "length", new BNumber(734003200) },
            { "private", new BNumber(1) }
        };

        var result = InfoHashCalculator.Calculate(info);

        Assert.That(result, Has.Length.EqualTo(40));
        Assert.That(result, Does.Match("^[0-9a-f]{40}$"));
    }

    [Test]
    public void Calculate_should_only_contain_valid_hex_characters()
    {
        var info = new BDictionary
        {
            { "data", new BString("anything") }
        };

        var result = InfoHashCalculator.Calculate(info);

        Assert.That(result, Does.Match("^[0-9a-f]+$"));
    }

    [Test]
    public void Calculate_raw_bytes_should_return_sha1_hex_string_for_known_input()
    {
        var rawBytes = "d4:name5:helloe"u8;
        var expectedHash = Convert.ToHexString(SHA1.HashData(rawBytes)).ToLowerInvariant();

        var result = InfoHashCalculator.Calculate(rawBytes);

        Assert.That(result, Is.EqualTo(expectedHash));
    }

    [Test]
    public void Calculate_raw_bytes_should_maintain_authentic_hash_for_non_canonical_order()
    {
        // Non-canonical key order: "name" before "length"
        var rawBytes = "d4:name8:test.txt6:lengthi1024e12:piece lengthi16384e6:pieces20:12345678901234567890e"u8;
        var expectedHash = Convert.ToHexString(SHA1.HashData(rawBytes)).ToLowerInvariant();

        var result = InfoHashCalculator.Calculate(rawBytes);

        Assert.That(result, Is.EqualTo(expectedHash));
    }

    [Test]
    public void Calculate_should_fallback_to_dictionary_encoding_when_raw_bytes_are_not_provided()
    {
        var info = new BDictionary
        {
            { "name", new BString("fallback-test") },
            { "piece length", new BNumber(16384) }
        };

        var encoded = info.EncodeAsBytes();
        var expectedHash = Convert.ToHexString(SHA1.HashData(encoded)).ToLowerInvariant();

        var result = InfoHashCalculator.Calculate(info);

        Assert.That(result, Is.EqualTo(expectedHash));
    }

    [Test]
    public void Calculate_dictionary_should_throw_when_null()
    {
        Assert.Throws<ArgumentNullException>(() => InfoHashCalculator.Calculate(null as BDictionary));
    }

    [Test]
    public void CalculateV2_should_return_sha256_hex_string_for_known_input()
    {
        var info = new BDictionary
        {
            { "name", new BString("hello") }
        };

        var result = InfoHashCalculator.CalculateV2(info);

        var encoded = info.EncodeAsBytes();
        var expectedHash = SHA256.HashData(encoded);
        var expected = Convert.ToHexString(expectedHash).ToLowerInvariant();

        Assert.That(result, Is.EqualTo(expected));
        Assert.That(result, Has.Length.EqualTo(64));
        Assert.That(result, Does.Match("^[0-9a-f]{64}$"));
    }

    [Test]
    public void CalculateV2_raw_bytes_should_return_sha256_hex_string()
    {
        var rawBytes = "d4:name5:helloe"u8;
        var expectedHash = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();

        var result = InfoHashCalculator.CalculateV2(rawBytes);

        Assert.That(result, Is.EqualTo(expectedHash));
        Assert.That(result, Has.Length.EqualTo(64));
    }

    [Test]
    public void CalculateV2_dictionary_should_throw_when_null()
    {
        Assert.Throws<ArgumentNullException>(() => InfoHashCalculator.CalculateV2(null as BDictionary));
    }

    [Test]
    public void Calculate_with_out_params_should_return_both_v1_and_v2_hashes()
    {
        var info = new BDictionary
        {
            { "name", new BString("hybrid-torrent") },
            { "piece length", new BNumber(32768) }
        };

        InfoHashCalculator.Calculate(info, out var v1Hash, out var v2Hash);

        var encoded = info.EncodeAsBytes();
        var expectedV1 = Convert.ToHexString(SHA1.HashData(encoded)).ToLowerInvariant();
        var expectedV2 = Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant();

        Assert.That(v1Hash, Is.EqualTo(expectedV1));
        Assert.That(v1Hash, Has.Length.EqualTo(40));
        Assert.That(v2Hash, Is.EqualTo(expectedV2));
        Assert.That(v2Hash, Has.Length.EqualTo(64));
    }

    [Test]
    public void Calculate_raw_bytes_with_out_params_should_return_both_v1_and_v2_hashes()
    {
        var rawBytes = "d4:name6:hybrid12:piece lengthi16384ee"u8;

        InfoHashCalculator.Calculate(rawBytes, out var v1Hash, out var v2Hash);

        var expectedV1 = Convert.ToHexString(SHA1.HashData(rawBytes)).ToLowerInvariant();
        var expectedV2 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();

        Assert.That(v1Hash, Is.EqualTo(expectedV1));
        Assert.That(v1Hash, Has.Length.EqualTo(40));
        Assert.That(v2Hash, Is.EqualTo(expectedV2));
        Assert.That(v2Hash, Has.Length.EqualTo(64));
    }

    [Test]
    public void Calculate_with_out_params_dictionary_should_throw_when_null()
    {
        Assert.Throws<ArgumentNullException>(() => InfoHashCalculator.Calculate(null as BDictionary, out _, out _));
    }
}
