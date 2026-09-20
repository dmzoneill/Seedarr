using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NzbDrone.Core.Subtitles;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Subtitles;

[TestFixture]
public class SubtitleDiscoveryServiceTest
{
    private SubtitleDiscoveryService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new SubtitleDiscoveryService();
    }

    [Test]
    public void DiscoverSubtitles_SameDirectory_MatchesStemPrefixAndLanguages()
    {
        var video = new TorrentFile { Id = 1, Path = "Inception.2010.1080p.mkv" };
        var files = new List<TorrentFile>
        {
            video,
            new TorrentFile { Id = 2, Path = "Inception.2010.1080p.en.srt" },
            new TorrentFile { Id = 3, Path = "Inception.2010.1080p.fr.forced.srt" },
            new TorrentFile { Id = 4, Path = "Inception.2010.1080p.spa.vtt" }
        };

        var tracks = _service.DiscoverSubtitles(video, files);

        Assert.That(tracks.Count, Is.EqualTo(3));

        var enTrack = tracks.FirstOrDefault(t => t.TwoLetterCode == "en");
        Assert.That(enTrack, Is.Not.Null);
        Assert.That(enTrack.Title, Is.EqualTo("English"));
        Assert.That(enTrack.Format, Is.EqualTo("srt"));
        Assert.That(enTrack.IsForced, Is.False);

        var frTrack = tracks.FirstOrDefault(t => t.TwoLetterCode == "fr");
        Assert.That(frTrack, Is.Not.Null);
        Assert.That(frTrack.Title, Does.Contain("French"));
        Assert.That(frTrack.IsForced, Is.True);

        var esTrack = tracks.FirstOrDefault(t => t.TwoLetterCode == "es");
        Assert.That(esTrack, Is.Not.Null);
        Assert.That(esTrack.Title, Is.EqualTo("Spanish"));
        Assert.That(esTrack.Format, Is.EqualTo("vtt"));
    }

    [Test]
    public void DiscoverSubtitles_Subdirectory_SubsFolder_AssociatesWithVideo()
    {
        var video = new TorrentFile { Id = 1, Path = "Movie (2021)/Movie (2021).mp4" };
        var files = new List<TorrentFile>
        {
            video,
            new TorrentFile { Id = 2, Path = "Movie (2021)/Subs/2_English.srt" },
            new TorrentFile { Id = 3, Path = "Movie (2021)/Subs/3_Spanish.srt" }
        };

        var tracks = _service.DiscoverSubtitles(video, files);

        Assert.That(tracks.Count, Is.EqualTo(2));
        Assert.That(tracks[0].TwoLetterCode, Is.EqualTo("en"));
        Assert.That(tracks[0].Title, Is.EqualTo("English"));
        Assert.That(tracks[1].TwoLetterCode, Is.EqualTo("es"));
        Assert.That(tracks[1].Title, Is.EqualTo("Spanish"));
    }

    [Test]
    public void DiscoverSubtitles_SingleVideoTorrent_AssociatesAllSubsInPayload()
    {
        var video = new TorrentFile { Id = 1, Path = "SingleMovie.mkv" };
        var files = new List<TorrentFile>
        {
            video,
            new TorrentFile { Id = 2, Path = "Subs/English.srt" },
            new TorrentFile { Id = 3, Path = "OtherFolder/Spanish.vtt" }
        };

        var tracks = _service.DiscoverSubtitles(video, files);

        Assert.That(tracks.Count, Is.EqualTo(2));
        Assert.That(tracks.Any(t => t.TwoLetterCode == "en"), Is.True);
        Assert.That(tracks.Any(t => t.TwoLetterCode == "es"), Is.True);
    }

    [Test]
    public void DiscoverSubtitles_TvSeries_MatchesEpisodeCodesCorrectly()
    {
        var ep1 = new TorrentFile { Id = 1, Path = "Series S01/Show.S01E01.mkv" };
        var ep2 = new TorrentFile { Id = 2, Path = "Series S01/Show.S01E02.mkv" };

        var files = new List<TorrentFile>
        {
            ep1,
            ep2,
            new TorrentFile { Id = 3, Path = "Series S01/Show.S01E01.en.srt" },
            new TorrentFile { Id = 4, Path = "Series S01/Show.S01E01.fr.srt" },
            new TorrentFile { Id = 5, Path = "Series S01/Show.S01E02.en.srt" },
            new TorrentFile { Id = 6, Path = "Series S01/Subs/Show.S01E02/2_German.srt" }
        };

        var ep1Tracks = _service.DiscoverSubtitles(ep1, files);
        Assert.That(ep1Tracks.Count, Is.EqualTo(2));
        Assert.That(ep1Tracks.All(t => t.Path.Contains("S01E01")), Is.True);

        var ep2Tracks = _service.DiscoverSubtitles(ep2, files);
        Assert.That(ep2Tracks.Count, Is.EqualTo(2));
        Assert.That(ep2Tracks.All(t => t.Path.Contains("S01E02")), Is.True);
        Assert.That(ep2Tracks.Any(t => t.TwoLetterCode == "de"), Is.True);
    }

    [Test]
    public void DiscoverSubtitles_DetectsSdhHearingImpaired()
    {
        var video = new TorrentFile { Id = 1, Path = "Movie.mkv" };
        var files = new List<TorrentFile>
        {
            video,
            new TorrentFile { Id = 2, Path = "Movie.en.sdh.srt" }
        };

        var tracks = _service.DiscoverSubtitles(video, files);
        Assert.That(tracks.Count, Is.EqualTo(1));
        Assert.That(tracks[0].IsHearingImpaired, Is.True);
        Assert.That(tracks[0].Title, Does.Contain("SDH"));
    }

    [Test]
    public void IsSubtitleFile_RecognizesValidExtensions()
    {
        Assert.That(_service.IsSubtitleFile("sub.srt"), Is.True);
        Assert.That(_service.IsSubtitleFile("sub.vtt"), Is.True);
        Assert.That(_service.IsSubtitleFile("sub.sub"), Is.True);
        Assert.That(_service.IsSubtitleFile("movie.mkv"), Is.False);
    }
}
