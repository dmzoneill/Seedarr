using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Notifications;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class EpisodicParserTest
{
    private EpisodicParser _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new EpisodicParser();
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_single_episode_standard()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("The.Show.S02E05.720p.HDTV");

        Assert.That(result.SeasonNumber, Is.EqualTo(2));
        Assert.That(result.EpisodeNumber, Is.EqualTo(5));
        Assert.That(result.EndingEpisodeNumber, Is.Null);
        Assert.That(result.EpisodeNumbers, Is.EqualTo(new List<int> { 5 }));
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_single_episode_alt_format()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("The.Show.1x04.HDTV");

        Assert.That(result.SeasonNumber, Is.EqualTo(1));
        Assert.That(result.EpisodeNumber, Is.EqualTo(4));
        Assert.That(result.EndingEpisodeNumber, Is.Null);
        Assert.That(result.EpisodeNumbers, Is.EqualTo(new List<int> { 4 }));
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_multi_episode_range_with_E_prefix()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("The.Show.S01E01-E03.1080p");

        Assert.That(result.SeasonNumber, Is.EqualTo(1));
        Assert.That(result.EpisodeNumber, Is.EqualTo(1));
        Assert.That(result.EndingEpisodeNumber, Is.EqualTo(3));
        Assert.That(result.EpisodeNumbers, Is.EqualTo(new List<int> { 1, 2, 3 }));
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_multi_episode_range_without_second_E()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("The.Show.S03E07-09.720p");

        Assert.That(result.SeasonNumber, Is.EqualTo(3));
        Assert.That(result.EpisodeNumber, Is.EqualTo(7));
        Assert.That(result.EndingEpisodeNumber, Is.EqualTo(9));
        Assert.That(result.EpisodeNumbers, Is.EqualTo(new List<int> { 7, 8, 9 }));
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_multi_episode_alt_range()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("The.Show.2x01-03.HDTV");

        Assert.That(result.SeasonNumber, Is.EqualTo(2));
        Assert.That(result.EpisodeNumber, Is.EqualTo(1));
        Assert.That(result.EndingEpisodeNumber, Is.EqualTo(3));
        Assert.That(result.EpisodeNumbers, Is.EqualTo(new List<int> { 1, 2, 3 }));
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_multi_episode_repeated()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("The.Show.S01E01E02.1080p");

        Assert.That(result.SeasonNumber, Is.EqualTo(1));
        Assert.That(result.EpisodeNumber, Is.EqualTo(1));
        Assert.That(result.EndingEpisodeNumber, Is.EqualTo(2));
        Assert.That(result.EpisodeNumbers, Is.EqualTo(new List<int> { 1, 2 }));
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_season_pack_with_complete()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("Show.Name.S02.Complete.1080p");

        Assert.That(result.SeasonNumber, Is.EqualTo(2));
        Assert.That(result.EpisodeNumber, Is.Null);
        Assert.That(result.EndingEpisodeNumber, Is.Null);
        Assert.That(result.EpisodeNumbers, Is.Empty);
        Assert.That(result.IsSeasonPack, Is.True);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_season_pack_with_named_season()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("Show.Season.2");

        Assert.That(result.SeasonNumber, Is.EqualTo(2));
        Assert.That(result.EpisodeNumber, Is.Null);
        Assert.That(result.EpisodeNumbers, Is.Empty);
        Assert.That(result.IsSeasonPack, Is.True);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_season_range_pack()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("Show.Name.S01-S03.1080p");

        Assert.That(result.SeasonNumber, Is.EqualTo(1));
        Assert.That(result.EpisodeNumber, Is.Null);
        Assert.That(result.EpisodeNumbers, Is.Empty);
        Assert.That(result.IsSeasonPack, Is.True);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_daily_show()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("The.Daily.Show.2024.01.15.720p");

        Assert.That(result.AirDate, Is.EqualTo("2024-01-15"));
        Assert.That(result.SeasonNumber, Is.Null);
        Assert.That(result.EpisodeNumber, Is.Null);
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_anime_absolute_numbering()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("[SubsPlease] One Piece - 1089 (1080p) [12345678].mkv");

        Assert.That(result.AbsoluteEpisodeNumber, Is.EqualTo(1089));
        Assert.That(result.EpisodeNumber, Is.EqualTo(1089));
        Assert.That(result.EpisodeNumbers, Is.EqualTo(new List<int> { 1089 }));
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_parse_anime_with_explicit_ep()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("Anime Show Name Ep 42");

        Assert.That(result.AbsoluteEpisodeNumber, Is.EqualTo(42));
        Assert.That(result.EpisodeNumber, Is.EqualTo(42));
        Assert.That(result.EpisodeNumbers, Is.EqualTo(new List<int> { 42 }));
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicReleaseInfo_should_not_mistake_release_year_for_anime_episode()
    {
        var result = _subject.ExtractEpisodicReleaseInfo("Movie.Name - 2024 - 1080p.BluRay");

        Assert.That(result.AbsoluteEpisodeNumber, Is.Null);
        Assert.That(result.EpisodeNumber, Is.Null);
        Assert.That(result.IsSeasonPack, Is.False);
    }

    [Test]
    public void ExtractEpisodicInfo_backward_compatibility_matches_legacy_tuple()
    {
        var (s1, e1, title1) = _subject.ExtractEpisodicInfo("Show.Name.S03E08.720p");
        Assert.That(s1, Is.EqualTo(3));
        Assert.That(e1, Is.EqualTo(8));
        Assert.That(title1, Is.Null);

        var (s2, e2, title2) = _subject.ExtractEpisodicInfo("Show.Name.2x05.720p");
        Assert.That(s2, Is.EqualTo(2));
        Assert.That(e2, Is.EqualTo(5));
        Assert.That(title2, Is.Null);

        var (s3, e3, title3) = _subject.ExtractEpisodicInfo(null);
        Assert.That(s3, Is.Null);
        Assert.That(e3, Is.Null);
        Assert.That(title3, Is.Null);
    }
}
