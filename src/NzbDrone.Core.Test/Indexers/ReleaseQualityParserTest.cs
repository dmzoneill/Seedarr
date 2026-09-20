using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class ReleaseQualityParserTest
{
    [TestCase("Dune.Part.Two.2024.2160p.UHD.Remux.HEVC.DV.TrueHD.Atmos.7.1-FraMeSToR", "2160p", "Remux", "HEVC", "7.1", "TrueHD")]
    [TestCase("Breaking.Bad.S05E16.1080p.BluRay.x264.DTS-HD.MA.5.1-ROVERS", "1080p", "BluRay", "AVC", "5.1", "DTS-HD MA")]
    [TestCase("Ted.Lasso.S03E01.720p.WEB-DL.x265.AAC.2.0-NTb", "720p", "WEB-DL", "HEVC", "2.0", "AAC")]
    [TestCase("The.Wire.S01E01.480p.DVDRip.x264-GRP", "SD", "DVD", "AVC", null, null)]
    [TestCase("Cosmos.A.Personal.Voyage.S01.576p.DVD-R.DD2.0.x264", "SD", "DVD", "AVC", "2.0", "AC3")]
    [TestCase("Cyberpunk.Edgerunners.S01.1080p.NF.WEBRip.AV1.Opus.5.1", "1080p", "WEBRip", "AV1", "5.1", "Opus")]
    [TestCase("Big.Bang.Theory.S12E24.720p.HDTV.H.264.DD5.1", "720p", "HDTV", "AVC", "5.1", "AC3")]
    [TestCase("Classic.Movie.1960.BDRip.XviD.AC3-WAF", null, "BluRay", "XviD", null, "AC3")]
    public void Parse_should_extract_quality_attributes_correctly(
        string title, string expectedRes, string expectedSource, string expectedCodec, string expectedChannels, string expectedAudio)
    {
        var quality = ReleaseQualityParser.Parse(title);

        Assert.That(quality.Resolution, Is.EqualTo(expectedRes));
        Assert.That(quality.Source, Is.EqualTo(expectedSource));
        Assert.That(quality.Codec, Is.EqualTo(expectedCodec));
        if (expectedChannels != null)
        {
            Assert.That(quality.AudioChannels, Is.EqualTo(expectedChannels));
        }

        if (expectedAudio != null)
        {
            Assert.That(quality.AudioCodec, Is.EqualTo(expectedAudio));
        }
    }

    [TestCase("2160p", "4k", true)]
    [TestCase("2160p", "UHD", true)]
    [TestCase("2160p", "2160p", true)]
    [TestCase("2160p", "1080p", false)]
    [TestCase("1080p", "1080p", true)]
    [TestCase("1080p", "1080i", true)]
    [TestCase("1080p", "720p", false)]
    [TestCase("720p", "720p", true)]
    [TestCase("720p", "HD", true)]
    [TestCase("SD", "480p", true)]
    [TestCase("SD", "576p", true)]
    [TestCase("SD", "SD", true)]
    [TestCase("SD", "1080p", false)]
    public void MatchesResolution_should_match_synonyms(string parsedRes, string allowedRes, bool shouldMatch)
    {
        var result = ReleaseQualityParser.MatchesResolution(parsedRes, new[] { allowedRes });
        Assert.That(result, Is.EqualTo(shouldMatch));
    }

    [TestCase("Remux", "Remux", true)]
    [TestCase("Remux", "BD-Remux", true)]
    [TestCase("Remux", "BluRay", false)]
    [TestCase("BluRay", "BluRay", true)]
    [TestCase("BluRay", "Blu-Ray", true)]
    [TestCase("BluRay", "BDRip", true)]
    [TestCase("WEB-DL", "WEB-DL", true)]
    [TestCase("WEB-DL", "WEBDL", true)]
    [TestCase("WEB-DL", "WEBRip", false)]
    [TestCase("WEBRip", "WEBRip", true)]
    [TestCase("WEBRip", "WEB-Rip", true)]
    [TestCase("HDTV", "HDTV", true)]
    [TestCase("DVD", "DVDRip", true)]
    public void MatchesSource_should_match_synonyms(string parsedSource, string allowedSource, bool shouldMatch)
    {
        var result = ReleaseQualityParser.MatchesSource(parsedSource, new[] { allowedSource });
        Assert.That(result, Is.EqualTo(shouldMatch));
    }

    [TestCase("HEVC", "HEVC", true)]
    [TestCase("HEVC", "x265", true)]
    [TestCase("HEVC", "h265", true)]
    [TestCase("HEVC", "H.265", true)]
    [TestCase("HEVC", "HEVC/x265", true)]
    [TestCase("HEVC", "x264", false)]
    [TestCase("AVC", "x264", true)]
    [TestCase("AVC", "AVC", true)]
    [TestCase("AVC", "h264", true)]
    [TestCase("AVC", "AVC/x264", true)]
    [TestCase("AVC", "AV1", false)]
    [TestCase("AV1", "AV1", true)]
    [TestCase("XviD", "XviD", true)]
    [TestCase("XviD", "DivX", true)]
    public void MatchesCodec_should_match_synonyms(string parsedCodec, string allowedCodec, bool shouldMatch)
    {
        var result = ReleaseQualityParser.MatchesCodec(parsedCodec, new[] { allowedCodec });
        Assert.That(result, Is.EqualTo(shouldMatch));
    }

    [Test]
    public void Matches_methods_should_allow_all_when_filter_empty_or_null()
    {
        Assert.That(ReleaseQualityParser.MatchesResolution("1080p", null), Is.True);
        Assert.That(ReleaseQualityParser.MatchesResolution("1080p", new List<string>()), Is.True);
        Assert.That(ReleaseQualityParser.MatchesSource("BluRay", null), Is.True);
        Assert.That(ReleaseQualityParser.MatchesSource("BluRay", new List<string>()), Is.True);
        Assert.That(ReleaseQualityParser.MatchesCodec("AVC", null), Is.True);
        Assert.That(ReleaseQualityParser.MatchesCodec("AVC", new List<string>()), Is.True);
    }
}
