using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment;

namespace NzbDrone.Core.Test.MediaEnrichment;

[TestFixture]
public class ReleaseTitleParserTest
{
    [TestCase("The.Matrix.1999.2160p.UHD.BluRay.x265-GROUP.mkv", "The Matrix", 1999, "2160p", "BluRay", "x265", "GROUP")]
    [TestCase("Inception.2010.1080p.BluRay.x264-SPARKS.mp4", "Inception", 2010, "1080p", "BluRay", "x264", "SPARKS")]
    [TestCase("Dune.Part.Two.2024.2160p.UHD.Remux.mkv", "Dune Part Two", 2024, "2160p", "Remux", null, null)]
    [TestCase("Blade.Runner.2049.2017.1080p.BluRay.x264-ROVERS", "Blade Runner 2049", 2017, "1080p", "BluRay", "x264", "ROVERS")]
    public void Parse_StandardMovieSceneReleases_ExtractsStructuredAttributes(
        string raw,
        string expectedTitle,
        int? expectedYear,
        string expectedResolution,
        string expectedSource,
        string expectedCodec,
        string expectedGroup)
    {
        var result = ReleaseTitleParser.Parse(raw);

        Assert.That(result.CleanTitle, Is.EqualTo(expectedTitle));
        Assert.That(result.Year, Is.EqualTo(expectedYear));

        if (expectedResolution != null)
        {
            Assert.That(result.Resolution, Is.EqualTo(expectedResolution));
        }

        if (expectedSource != null)
        {
            Assert.That(result.Source, Is.EqualTo(expectedSource));
        }

        if (expectedCodec != null)
        {
            Assert.That(result.Codec, Is.EqualTo(expectedCodec));
        }

        if (expectedGroup != null)
        {
            Assert.That(result.ReleaseGroup, Is.EqualTo(expectedGroup));
        }
    }

    [Test]
    public void Parse_StandardTvSceneReleases_ExtractsSeasonAndEpisode()
    {
        var tv1 = ReleaseTitleParser.Parse("Severance.S01E01.1080p.WEB-DL.x265-FLUX.mkv");
        Assert.That(tv1.CleanTitle, Is.EqualTo("Severance"));
        Assert.That(tv1.SeasonNumber, Is.EqualTo(1));
        Assert.That(tv1.EpisodeNumber, Is.EqualTo(1));
        Assert.That(tv1.Resolution, Is.EqualTo("1080p"));
        Assert.That(tv1.Source, Is.EqualTo("WEB-DL"));
        Assert.That(tv1.Codec, Is.EqualTo("x265"));
        Assert.That(tv1.ReleaseGroup, Is.EqualTo("FLUX"));
        Assert.That(tv1.ReleaseType, Is.EqualTo("Series"));

        var tv2 = ReleaseTitleParser.Parse("The.Penguin.S01.720p.HDTV.x264-SPARKS");
        Assert.That(tv2.CleanTitle, Is.EqualTo("The Penguin"));
        Assert.That(tv2.SeasonNumber, Is.EqualTo(1));
        Assert.That(tv2.Resolution, Is.EqualTo("720p"));
        Assert.That(tv2.Source, Is.EqualTo("HDTV"));
        Assert.That(tv2.ReleaseGroup, Is.EqualTo("SPARKS"));

        var tv3 = ReleaseTitleParser.Parse("Chernobyl.Season.1.Complete.1080p.BluRay");
        Assert.That(tv3.CleanTitle, Is.EqualTo("Chernobyl"));
        Assert.That(tv3.SeasonNumber, Is.EqualTo(1));
        Assert.That(tv3.Resolution, Is.EqualTo("1080p"));
        Assert.That(tv3.Source, Is.EqualTo("BluRay"));
    }

    [Test]
    public void Parse_AnimeFansubReleases_StripsBracketsAndCrc32()
    {
        var anime1 = ReleaseTitleParser.Parse("[SubsPlease] Sousou no Frieren - 01 (1080p) [F2E4A9B1].mkv");
        Assert.That(anime1.CleanTitle, Is.EqualTo("Sousou no Frieren"));
        Assert.That(anime1.EpisodeNumber, Is.EqualTo(1));
        Assert.That(anime1.Resolution, Is.EqualTo("1080p"));
        Assert.That(anime1.ReleaseGroup, Is.EqualTo("SubsPlease"));
        Assert.That(anime1.ReleaseType, Is.EqualTo("Anime"));

        var anime2 = ReleaseTitleParser.Parse("[Erai-raws] Jujutsu Kaisen 2nd Season - 14 [1080p][Multiple Subtitle] [A1B2C3D4].mkv");
        Assert.That(anime2.CleanTitle, Is.EqualTo("Jujutsu Kaisen"));
        Assert.That(anime2.SeasonNumber, Is.EqualTo(2));
        Assert.That(anime2.EpisodeNumber, Is.EqualTo(14));
        Assert.That(anime2.Resolution, Is.EqualTo("1080p"));
        Assert.That(anime2.ReleaseGroup, Is.EqualTo("Erai-raws"));
        Assert.That(anime2.ReleaseType, Is.EqualTo("Anime"));
    }

    [Test]
    public void Parse_TitlesWithSensitiveKeywords_ProtectsAgainstFalsePositiveTruncation()
    {
        var t1 = ReleaseTitleParser.Parse("Charlotte's Web (2006)");
        Assert.That(t1.CleanTitle, Is.EqualTo("Charlotte's Web"));
        Assert.That(t1.Year, Is.EqualTo(2006));

        var t2 = ReleaseTitleParser.Parse("A Season in Hell (1991)");
        Assert.That(t2.CleanTitle, Is.EqualTo("A Season in Hell"));
        Assert.That(t2.Year, Is.EqualTo(1991));

        var t3 = ReleaseTitleParser.Parse("The Complete Walk (2016)");
        Assert.That(t3.CleanTitle, Is.EqualTo("The Complete Walk"));
        Assert.That(t3.Year, Is.EqualTo(2016));

        var t4 = ReleaseTitleParser.Parse("Charlotte's.Web.2006.1080p.BluRay.x264-GROUP");
        Assert.That(t4.CleanTitle, Is.EqualTo("Charlotte's Web"));
        Assert.That(t4.Year, Is.EqualTo(2006));
        Assert.That(t4.Resolution, Is.EqualTo("1080p"));
        Assert.That(t4.Source, Is.EqualTo("BluRay"));
        Assert.That(t4.Codec, Is.EqualTo("x264"));
        Assert.That(t4.ReleaseGroup, Is.EqualTo("GROUP"));
    }

    [Test]
    public void Parse_TitlesWithNumericPrefixOrYearInTitle()
    {
        var m1 = ReleaseTitleParser.Parse("2001.A.Space.Odyssey.1968.1080p.BluRay.x264-GROUP");
        Assert.That(m1.CleanTitle, Is.EqualTo("2001 A Space Odyssey"));
        Assert.That(m1.Year, Is.EqualTo(1968));

        var m2 = ReleaseTitleParser.Parse("1917.2019.2160p.UHD.BluRay.x265-GROUP");
        Assert.That(m2.CleanTitle, Is.EqualTo("1917"));
        Assert.That(m2.Year, Is.EqualTo(2019));
    }

    [Test]
    public void Parse_AudioAndCodecExtraction_IdentifiesAttributes()
    {
        var res = ReleaseTitleParser.Parse("Movie.2023.1080p.BluRay.DTS-HD.MA.x264-GROUP");
        Assert.That(res.Audio, Is.EqualTo("DTS-HD"));
        Assert.That(res.Codec, Is.EqualTo("x264"));

        var atmos = ReleaseTitleParser.Parse("Movie.2023.2160p.UHD.Atmos.HEVC-GROUP");
        Assert.That(atmos.Audio, Is.EqualTo("Atmos"));
        Assert.That(atmos.Codec, Is.EqualTo("x265"));
    }

    [TestCase("", "")]
    [TestCase(null, "")]
    [TestCase("   ", "")]
    public void Parse_EmptyOrNullTitle_ReturnsEmptyCleanTitle(string raw, string expected)
    {
        var result = ReleaseTitleParser.Parse(raw);
        Assert.That(result.CleanTitle, Is.EqualTo(expected));
    }
}
