using System.IO;
using NUnit.Framework;
using NzbDrone.Core.MediaInspection;

namespace NzbDrone.Core.Test.MediaInspection;

[TestFixture]
public class MediaContainerInspectorTest
{
    private MediaContainerInspector _inspector;

    [SetUp]
    public void SetUp()
    {
        _inspector = new MediaContainerInspector();
    }

    [TestCase("movie.mkv", "Matroska")]
    [TestCase("movie.mp4", "MPEG-4")]
    [TestCase("movie.m4v", "MPEG-4")]
    [TestCase("movie.avi", "AVI")]
    [TestCase("movie.mov", "QuickTime")]
    [TestCase("movie.wmv", "Windows Media")]
    [TestCase("movie.flv", "Flash Video")]
    [TestCase("movie.webm", "WebM")]
    [TestCase("movie.ts", "MPEG-TS")]
    [TestCase("movie.m2ts", "MPEG-TS")]
    [TestCase("track.flac", "FLAC")]
    [TestCase("track.mp3", "MP3")]
    [TestCase("track.aac", "AAC")]
    [TestCase("track.ogg", "Ogg")]
    [TestCase("track.oga", "Ogg")]
    [TestCase("track.wav", "WAV")]
    [TestCase("movie.MKV", "Matroska")]
    [TestCase("movie.Mp4", "MPEG-4")]
    [TestCase("movie.TS", "MPEG-TS")]
    public void InspectFileName_WithRecognizedMediaExtension_SetsContainerFormat(string fileName, string expectedFormat)
    {
        var result = MediaContainerInspector.InspectFileName(fileName);

        Assert.That(result.ContainerFormat, Is.EqualTo(expectedFormat));
    }

    [TestCase("Show.Title.1080p.BluRay.x264-ROVERS")]
    [TestCase("Movie.2023.1080p.WEB-DL.DDP5.1.Atmos.H.264-FLUX")]
    [TestCase("Some.Release.INTERNAL")]
    [TestCase("Some.Release.REPACK")]
    [TestCase("file.exe")]
    [TestCase("file.txt")]
    [TestCase("file.nfo")]
    [TestCase("file.srt")]
    [TestCase("file_without_extension")]
    public void InspectFileName_WithUnrecognizedExtensionOrSceneTag_LeavesContainerFormatNull(string fileName)
    {
        var result = MediaContainerInspector.InspectFileName(fileName);

        Assert.That(result.ContainerFormat, Is.Null);
    }

    [Test]
    public void Inspect_WithSceneReleaseTag_ReturnsNullContainerFormat()
    {
        using var stream = new MemoryStream(new byte[8]);
        var result = _inspector.Inspect(stream, "Show.Title.1080p.BluRay.x264-ROVERS");

        Assert.That(result.ContainerFormat, Is.Null);
        Assert.That(result.Resolution, Is.EqualTo("1080p"));
        Assert.That(result.VideoCodec, Is.EqualTo("AVC"));
    }

    [Test]
    public void Inspect_WithLegitimateMediaFile_ReturnsExpectedContainerFormat()
    {
        using var stream = new MemoryStream(new byte[8]);
        var result = _inspector.Inspect(stream, "Show.Title.S01E01.1080p.mkv");

        Assert.That(result.ContainerFormat, Is.EqualTo("Matroska"));
        Assert.That(result.Resolution, Is.EqualTo("1080p"));
    }

    [Test]
    public void InspectFile_WithNullOrEmptyPath_ReturnsNull()
    {
        Assert.That(_inspector.InspectFile(null), Is.Null);
        Assert.That(_inspector.InspectFile(string.Empty), Is.Null);
        Assert.That(_inspector.InspectFile("   "), Is.Null);
    }
}
