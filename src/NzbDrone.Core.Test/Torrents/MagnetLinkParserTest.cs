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

    [Test]
    public void Parse_should_parse_pure_v2_btmh_magnet_link()
    {
        const string v2Hash = "2b8c52d38b5ae5548fc986b19ffe2121960e4c2626e9511075fc817a0c3b1d92";
        var magnetUri = $"magnet:?xt=urn:btmh:1220{v2Hash}&dn=PureV2Torrent";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.InfoHashV2, Is.EqualTo(v2Hash));
        Assert.That(result.InfoHash, Is.Null);
        Assert.That(result.Name, Is.EqualTo("PureV2Torrent"));
    }

    [Test]
    public void Parse_should_fall_back_to_v2_infohash_when_dn_is_missing()
    {
        const string v2Hash = "2b8c52d38b5ae5548fc986b19ffe2121960e4c2626e9511075fc817a0c3b1d92";
        var magnetUri = $"magnet:?xt=urn:btmh:1220{v2Hash}";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.Name, Is.EqualTo(v2Hash));
        Assert.That(result.InfoHashV2, Is.EqualTo(v2Hash));
    }

    [Test]
    public void Parse_should_parse_hybrid_magnet_link()
    {
        const string v2Hash = "2b8c52d38b5ae5548fc986b19ffe2121960e4c2626e9511075fc817a0c3b1d92";
        var magnetUri = $"magnet:?xt=urn:btih:{ValidInfoHash}&xt=urn:btmh:1220{v2Hash}&dn=HybridTorrent";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.InfoHash, Is.EqualTo(ValidInfoHash.ToLowerInvariant()));
        Assert.That(result.InfoHashV2, Is.EqualTo(v2Hash));
        Assert.That(result.Name, Is.EqualTo("HybridTorrent"));
    }

    [Test]
    public void Parse_should_parse_hybrid_magnet_link_with_v2_first()
    {
        const string v2Hash = "2b8c52d38b5ae5548fc986b19ffe2121960e4c2626e9511075fc817a0c3b1d92";
        var magnetUri = $"magnet:?xt=urn:btmh:1220{v2Hash}&xt=urn:btih:{ValidInfoHash}&dn=HybridTorrent";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.InfoHash, Is.EqualTo(ValidInfoHash.ToLowerInvariant()));
        Assert.That(result.InfoHashV2, Is.EqualTo(v2Hash));
        Assert.That(result.Name, Is.EqualTo("HybridTorrent"));
    }

    [Test]
    public void Parse_should_normalize_uppercase_v2_infohash_to_lowercase()
    {
        const string v2Hash = "2b8c52d38b5ae5548fc986b19ffe2121960e4c2626e9511075fc817a0c3b1d92";
        var magnetUri = $"magnet:?xt=urn:btmh:1220{v2Hash.ToUpperInvariant()}&dn=UpperV2";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.InfoHashV2, Is.EqualTo(v2Hash));
    }

    [Test]
    public void Parse_should_throw_when_btmh_prefix_is_invalid()
    {
        var magnetUri = "magnet:?xt=urn:btmh:99992b8c52d38b5ae5548fc986b19ffe2121960e4c2626e9511075fc817a0c3b1d92";

        var ex = Assert.Throws<ArgumentException>(() => MagnetLinkParser.Parse(magnetUri));
        Assert.That(ex.Message, Does.Contain("unsupported multihash prefix"));
    }

    [Test]
    public void Parse_should_throw_when_btmh_hex_is_invalid()
    {
        var magnetUri = "magnet:?xt=urn:btmh:1220notvalidhexcharacters";

        var ex = Assert.Throws<ArgumentException>(() => MagnetLinkParser.Parse(magnetUri));
        Assert.That(ex.Message, Does.Contain("v2 info hash must be 64 valid hexadecimal characters"));
    }

    [Test]
    public void Parse_should_extract_ws_parameters_into_webseeds_and_urllist()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{ValidInfoHash}&dn=WebSeedTest&ws=https%3A%2F%2Fwebseed1.example.com%2Ffiles%2F&ws=http%3A%2F%2Fwebseed2.example.com%2Ffile.iso";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.WebSeeds, Has.Count.EqualTo(2));
        Assert.That(result.WebSeeds[0], Is.EqualTo("https://webseed1.example.com/files/"));
        Assert.That(result.WebSeeds[1], Is.EqualTo("http://webseed2.example.com/file.iso"));
        Assert.That(result.UrlList, Is.EqualTo(result.WebSeeds));
    }

    [Test]
    public void Parse_should_deduplicate_and_filter_invalid_ws_urls()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{ValidInfoHash}&ws=https%3A%2F%2Fwebseed1.example.com%2Ffile.iso&ws=HTTPS%3A%2F%2FWEBSEED1.EXAMPLE.COM%2Ffile.iso&ws=invalid-url&ws=javascript:alert(1)&ws=";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.WebSeeds, Has.Count.EqualTo(1));
        Assert.That(result.WebSeeds[0], Is.EqualTo("https://webseed1.example.com/file.iso"));
    }

    [Test]
    public void Parse_should_return_empty_webseeds_when_no_ws_parameters()
    {
        var magnetUri = $"magnet:?xt=urn:btih:{ValidInfoHash}&dn=NoWebSeeds";

        var result = MagnetLinkParser.Parse(magnetUri);

        Assert.That(result.WebSeeds, Is.Not.Null);
        Assert.That(result.WebSeeds, Is.Empty);
        Assert.That(result.UrlList, Is.Empty);
    }
}
