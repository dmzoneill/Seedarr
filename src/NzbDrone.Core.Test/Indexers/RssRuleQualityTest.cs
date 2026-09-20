using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class RssRuleQualityTest
{
    [Test]
    public void Matches_with_AllowedResolutions_should_accept_matching_and_reject_others()
    {
        var rule = new RssRule
        {
            Name = "4K only",
            AllowedResolutions = new List<string> { "2160p" }
        };

        var release4k = new ReleaseInfo { Title = "Movie.Title.2024.2160p.WEB-DL.x265" };
        var release1080p = new ReleaseInfo { Title = "Movie.Title.2024.1080p.WEB-DL.x264" };

        Assert.That(rule.Matches(release4k), Is.True);
        Assert.That(rule.Matches(release1080p), Is.False);
    }

    [Test]
    public void Matches_with_AllowedSources_should_accept_matching_and_reject_others()
    {
        var rule = new RssRule
        {
            Name = "Remux only",
            AllowedSources = new List<string> { "Remux" }
        };

        var remux = new ReleaseInfo { Title = "Movie.Title.2024.1080p.BD-Remux.AVC" };
        var webdl = new ReleaseInfo { Title = "Movie.Title.2024.1080p.WEB-DL.x264" };

        Assert.That(rule.Matches(remux), Is.True);
        Assert.That(rule.Matches(webdl), Is.False);
    }

    [Test]
    public void Matches_with_AllowedCodecs_should_accept_matching_and_reject_others()
    {
        var rule = new RssRule
        {
            Name = "HEVC only",
            AllowedCodecs = new List<string> { "x265" }
        };

        var hevc = new ReleaseInfo { Title = "Show.S01E01.1080p.WEB-DL.HEVC" };
        var avc = new ReleaseInfo { Title = "Show.S01E01.1080p.WEB-DL.x264" };

        Assert.That(rule.Matches(hevc), Is.True);
        Assert.That(rule.Matches(avc), Is.False);
    }

    [Test]
    public void Matches_with_combination_of_quality_criteria()
    {
        var rule = new RssRule
        {
            Name = "1080p BluRay x264",
            AllowedResolutions = new List<string> { "1080p" },
            AllowedSources = new List<string> { "BluRay" },
            AllowedCodecs = new List<string> { "x264" }
        };

        var match = new ReleaseInfo { Title = "Movie.2024.1080p.BluRay.x264.DTS" };
        var wrongRes = new ReleaseInfo { Title = "Movie.2024.720p.BluRay.x264.DTS" };
        var wrongSource = new ReleaseInfo { Title = "Movie.2024.1080p.WEBRip.x264.DTS" };
        var wrongCodec = new ReleaseInfo { Title = "Movie.2024.1080p.BluRay.x265.DTS" };

        Assert.That(rule.Matches(match), Is.True);
        Assert.That(rule.Matches(wrongRes), Is.False);
        Assert.That(rule.Matches(wrongSource), Is.False);
        Assert.That(rule.Matches(wrongCodec), Is.False);
    }

    [Test]
    public void Matches_with_empty_quality_filters_should_accept_any_quality()
    {
        var rule = new RssRule
        {
            Name = "Catch-all"
        };

        var release1 = new ReleaseInfo { Title = "Any.Release.1080p.BluRay.x264" };
        var release2 = new ReleaseInfo { Title = "Unknown.Format.Title" };

        Assert.That(rule.Matches(release1), Is.True);
        Assert.That(rule.Matches(release2), Is.True);
    }
}
