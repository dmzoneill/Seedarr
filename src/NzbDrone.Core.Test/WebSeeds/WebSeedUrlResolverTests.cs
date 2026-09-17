using System;
using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.WebSeeds;

namespace NzbDrone.Core.Test.WebSeeds;

[TestFixture]
public class WebSeedUrlResolverTests
{
    private WebSeedUrlResolver _resolver;

    [SetUp]
    public void SetUp()
    {
        _resolver = new WebSeedUrlResolver();
    }

    [Test]
    public void EncodePathSegment_encodes_spaces_brackets_and_punctuation()
    {
        // Testing single segments
        var segment = "Band - Album [FLAC 24-bit]";
        var encoded = WebSeedUrlResolver.EncodePathSegment(segment);
        Assert.That(encoded, Is.EqualTo("Band%20-%20Album%20%5BFLAC%2024-bit%5D"));

        var trackSegment = "01. Track (Intro) [Live] \"Remix\".flac";
        var encodedTrack = WebSeedUrlResolver.EncodePathSegment(trackSegment);
        Assert.That(encodedTrack, Does.Contain("%5BLive%5D"));
        Assert.That(encodedTrack, Does.Contain("%20"));
        Assert.That(encodedTrack, Does.Contain("%22Remix%22"));
    }

    [Test]
    public void EncodePathSegment_encodes_unicode_japanese_chinese_cyrillic()
    {
        var japanese = "季節の映画";
        var encodedJapanese = WebSeedUrlResolver.EncodePathSegment(japanese);
        Assert.That(encodedJapanese, Is.EqualTo("%E5%AD%A3%E7%AF%80%E3%81%AE%E6%98%A0%E7%94%BB"));

        var chinese = "音乐下载";
        var encodedChinese = WebSeedUrlResolver.EncodePathSegment(chinese);
        Assert.That(encodedChinese, Is.EqualTo("%E9%9F%B3%E4%B9%90%E4%B8%8B%E8%BD%BD"));

        var cyrillic = "Музыка";
        var encodedCyrillic = WebSeedUrlResolver.EncodePathSegment(cyrillic);
        Assert.That(encodedCyrillic, Is.EqualTo("%D0%9C%D1%83%D0%B7%D1%8B%D0%BA%D0%B0"));
    }

    [Test]
    public void EncodePathSegment_normalizes_unicode_to_nfc()
    {
        // 'e' followed by combining acute accent (NFD)
        var nfd = "e\u0301";
        var encoded = WebSeedUrlResolver.EncodePathSegment(nfd);

        // Precomposed 'é' is %C3%A9 in UTF-8
        Assert.That(encoded, Is.EqualTo("%C3%A9"));
    }

    [Test]
    public void ResolveMultiFileUrl_resolves_segments_with_spaces_brackets_and_unicode()
    {
        var baseUrl = "https://webseed.example.com/downloads/";
        var segments = new[]
        {
            "Anime",
            "季節の映画",
            "Episode 01 [1080p].mkv"
        };

        var result = _resolver.ResolveMultiFileUrl(baseUrl, segments);

        Assert.That(result, Is.EqualTo("https://webseed.example.com/downloads/Anime/%E5%AD%A3%E7%AF%80%E3%81%AE%E6%98%A0%E7%94%BB/Episode%2001%20%5B1080p%5D.mkv"));
    }

    [Test]
    public void ResolveMultiFileUrl_normalizes_base_url_without_trailing_slash()
    {
        var baseUrl = "https://webseed.example.com/downloads";
        var segments = new[] { "folder", "file [v1].iso" };

        var result = _resolver.ResolveMultiFileUrl(baseUrl, segments);

        Assert.That(result, Is.EqualTo("https://webseed.example.com/downloads/folder/file%20%5Bv1%5D.iso"));
    }

    [Test]
    public void ResolveMultiFileUrl_sanitizes_leading_and_trailing_slashes_in_segments()
    {
        var baseUrl = "https://webseed.example.com/downloads/";
        var segments = new[] { "/folder/", "\\subfolder\\", "/file.txt" };

        var result = _resolver.ResolveMultiFileUrl(baseUrl, segments);

        Assert.That(result, Is.EqualTo("https://webseed.example.com/downloads/folder/subfolder/file.txt"));
    }

    [Test]
    public void ResolveMultiFileUrl_with_string_relative_path_resolves_correctly()
    {
        var baseUrl = "https://webseed.example.com/downloads/";
        var relativePath = "Music/Band - Album [FLAC]/01. Track [Live].flac";

        var result = _resolver.ResolveMultiFileUrl(baseUrl, relativePath);

        Assert.That(result, Is.EqualTo("https://webseed.example.com/downloads/Music/Band%20-%20Album%20%5BFLAC%5D/01.%20Track%20%5BLive%5D.flac"));
    }

    [Test]
    public void ResolveMultiFileUrl_rejects_path_traversal_attempts()
    {
        var baseUrl = "https://webseed.example.com/downloads/";

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveMultiFileUrl(baseUrl, new[] { ".." }));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveMultiFileUrl(baseUrl, new[] { "..", "etc", "passwd" }));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveMultiFileUrl(baseUrl, new[] { "../etc/passwd" }));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveMultiFileUrl(baseUrl, new[] { "folder/../secret.txt" }));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveMultiFileUrl(baseUrl, new[] { @"..\secret.txt" }));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveMultiFileUrl(baseUrl, "../etc/passwd"));
    }

    [Test]
    public void ResolveSingleFileUrl_when_base_url_ends_with_slash_appends_encoded_torrent_name()
    {
        var baseUrl = "https://webseed.example.com/files/";
        var torrentName = "Ubuntu 22.04 [Desktop x64].iso";

        var result = _resolver.ResolveSingleFileUrl(baseUrl, torrentName);

        Assert.That(result, Is.EqualTo("https://webseed.example.com/files/Ubuntu%2022.04%20%5BDesktop%20x64%5D.iso"));
    }

    [Test]
    public void ResolveSingleFileUrl_when_base_url_does_not_end_with_slash_returns_base_url_directly()
    {
        var baseUrl = "https://webseed.example.com/files/ubuntu-22.04.iso";
        var torrentName = "ubuntu-22.04.iso";

        var result = _resolver.ResolveSingleFileUrl(baseUrl, torrentName);

        Assert.That(result, Is.EqualTo("https://webseed.example.com/files/ubuntu-22.04.iso"));
    }

    [Test]
    public void ResolveSingleFileUrl_rejects_path_traversal_in_torrent_name()
    {
        var baseUrl = "https://webseed.example.com/files/";

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveSingleFileUrl(baseUrl, "../secret.iso"));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveSingleFileUrl(baseUrl, "..\\secret.iso"));
    }

    [Test]
    public void ResolveMethods_throw_on_invalid_or_empty_base_url()
    {
        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveMultiFileUrl(null, new[] { "file.txt" }));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveMultiFileUrl(string.Empty, new[] { "file.txt" }));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveMultiFileUrl("   ", new[] { "file.txt" }));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveSingleFileUrl(null, "file.txt"));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveSingleFileUrl(string.Empty, "file.txt"));

        Assert.Throws<ArgumentException>(() =>
            _resolver.ResolveSingleFileUrl("   ", "file.txt"));
    }
}
