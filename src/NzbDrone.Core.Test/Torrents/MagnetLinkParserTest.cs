using System;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class MagnetLinkParserTest
{
    private const string ValidInfoHash = "0123456789abcdef0123456789abcdef01234567";

    [Test]
    public void Parse_should_preserve_plus_character_in_tracker_urls()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{ValidInfoHash}&tr=https%3A%2F%2Ftracker.example.com%2Fannounce%3Fpasskey%3Dabc%2Bdef";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.Trackers, Has.Length.EqualTo(1));
        Assert.That(result.Trackers[0], Is.EqualTo("https://tracker.example.com/announce?passkey=abc+def"));
    }

    [Test]
    public void Parse_should_decode_display_name_exactly_once()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{ValidInfoHash}&dn=My%20Test%20Torrent%2BName";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.Name, Is.EqualTo("My Test Torrent+Name"));
    }

    [Test]
    public void Parse_should_throw_when_infohash_contains_non_hex_characters()
    {
        var invalidInfoHash = new string('g', 40);
        var magnetUri = $"magnet:?xt=urn:btih:{invalidInfoHash}";

        var ex = Assert.Throws<ArgumentException>(() => MagnetLinkParser.Parse(magnetUri));
        Assert.That(ex.Message, Does.Contain("info hash must be 40 valid hexadecimal characters"));
    }

    [Test]
    public void Parse_should_succeed_with_valid_40_char_hex_infohash()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{ValidInfoHash.ToUpperInvariant()}&dn=TestTorrent";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.InfoHash, Is.EqualTo(ValidInfoHash.ToLowerInvariant()));
        Assert.That(result.Name, Is.EqualTo("TestTorrent"));
    }

    [Test]
    public void Parse_should_fall_back_to_infohash_when_dn_is_missing_or_whitespace()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{ValidInfoHash}&dn=";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.Name, Is.EqualTo(ValidInfoHash));
    }

    [Test]
    public void Parse_should_filter_out_whitespace_trackers()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{ValidInfoHash}&tr=https%3A%2F%2Ftracker1.org%2Fannounce&tr=&tr=https%3A%2F%2Ftracker2.org%2Fannounce";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.Trackers, Is.EqualTo(new[]
        {
            "https://tracker1.org/announce",
            "https://tracker2.org/announce"
        }));
    }

    [Test]
    public void Parse_should_parse_base32_encoded_infohash()
    {
        // 32-char base32 infohash
        // "ONXW2ZJAMRQXIYJAONXW2ZJAMRQXIYJA"
        var base32Hash = "ONXW2ZJAMRQXIYJAONXW2ZJAMRQXIYJA";
        var magnetUri = $"magnet:?xt=urn:btih:{base32Hash}&dn=Base32Torrent";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.InfoHash.Length, Is.EqualTo(40));
        Assert.That(result.Name, Is.EqualTo("Base32Torrent"));
    }

    [Test]
    public void Parse_should_throw_when_no_query_parameters()
    {
        Assert.Throws<ArgumentException>(() => MagnetLinkParser.Parse("magnet:"));
    }

    [Test]
    public void Parse_should_throw_when_missing_btih()
    {
        Assert.Throws<ArgumentException>(() => MagnetLinkParser.Parse("magnet:?dn=Test"));
    }
}
