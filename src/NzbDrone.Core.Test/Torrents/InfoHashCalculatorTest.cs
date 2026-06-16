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
}
