using System;
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

    [Test]
    public void Inspect_WithEbmlMatroskaHeader_ReturnsMatroska()
    {
        var header = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(header);
        var result = _inspector.Inspect(stream, "video_without_extension");

        Assert.That(result.ContainerFormat, Is.EqualTo("Matroska"));
    }

    [Test]
    public void Inspect_WithEbmlWebmExtension_ReturnsWebM()
    {
        var header = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(header);
        var result = _inspector.Inspect(stream, "clip.webm");

        Assert.That(result.ContainerFormat, Is.EqualTo("WebM"));
    }

    [Test]
    public void Inspect_WithEbmlWebmDocTypeHeader_ReturnsWebM()
    {
        var header = new byte[]
        {
            0x1A, 0x45, 0xDF, 0xA3,
            0x42, 0x82, 0x84, (byte)'w', (byte)'e', (byte)'b', (byte)'m'
        };
        using var stream = new MemoryStream(header);
        var result = _inspector.Inspect(stream, "clip");

        Assert.That(result.ContainerFormat, Is.EqualTo("WebM"));
    }

    [TestCase("isom", "MPEG-4")]
    [TestCase("mp41", "MPEG-4")]
    [TestCase("mp42", "MPEG-4")]
    [TestCase("M4V ", "MPEG-4")]
    [TestCase("qt  ", "QuickTime")]
    public void Inspect_WithMp4FtypHeader_ReturnsExpectedContainerFormat(string brand, string expectedFormat)
    {
        var header = new byte[16];
        header[0] = 0;
        header[1] = 0;
        header[2] = 0;
        header[3] = 16;
        header[4] = (byte)'f';
        header[5] = (byte)'t';
        header[6] = (byte)'y';
        header[7] = (byte)'p';
        var brandBytes = System.Text.Encoding.ASCII.GetBytes(brand);
        Array.Copy(brandBytes, 0, header, 8, 4);

        using var stream = new MemoryStream(header);
        var result = _inspector.Inspect(stream, "sample.1080p");

        Assert.That(result.ContainerFormat, Is.EqualTo(expectedFormat));
        Assert.That(result.Resolution, Is.EqualTo("1080p"));
    }

    [Test]
    public void Inspect_WithFlacHeaderAndStreamInfo_ExtractsAudioProperties()
    {
        var data = new byte[38];
        data[0] = 0x66;
        data[1] = 0x4C;
        data[2] = 0x61;
        data[3] = 0x43;
        data[14] = 0x0A;
        data[15] = 0xC4;
        data[16] = 0x42;
        data[17] = 0xF0;

        using var stream = new MemoryStream(data);
        var result = _inspector.Inspect(stream, "track");

        Assert.That(result.ContainerFormat, Is.EqualTo("FLAC"));
        Assert.That(result.AudioCodec, Is.EqualTo("FLAC"));
        Assert.That(result.AudioSampleRate, Is.EqualTo(44100));
        Assert.That(result.AudioChannels, Is.EqualTo("2.0"));
        Assert.That(result.AudioBitDepth, Is.EqualTo(16));
    }

    [Test]
    public void Inspect_WithFlacHeaderAndBlockHeader_ExtractsAudioProperties()
    {
        var data = new byte[42];
        data[0] = 0x66;
        data[1] = 0x4C;
        data[2] = 0x61;
        data[3] = 0x43;
        data[4] = 0x00;
        data[5] = 0x00;
        data[6] = 0x00;
        data[7] = 0x22;
        data[18] = 0x0B;
        data[19] = 0xB8;
        data[20] = 0x0B;
        data[21] = 0x70;

        using var stream = new MemoryStream(data);
        var result = _inspector.Inspect(stream, "surround.flac");

        Assert.That(result.ContainerFormat, Is.EqualTo("FLAC"));
        Assert.That(result.AudioCodec, Is.EqualTo("FLAC"));
        Assert.That(result.AudioSampleRate, Is.EqualTo(48000));
        Assert.That(result.AudioChannels, Is.EqualTo("5.1"));
        Assert.That(result.AudioBitDepth, Is.EqualTo(24));
    }

    [Test]
    public void Inspect_WithId3v2Header_ReturnsMp3()
    {
        var id3Bytes = new byte[] { (byte)'I', (byte)'D', (byte)'3', 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(id3Bytes);
        var result = _inspector.Inspect(stream, "song_without_ext");

        Assert.That(result.ContainerFormat, Is.EqualTo("MP3"));
        Assert.That(result.AudioCodec, Is.EqualTo("MP3"));
    }

    [TestCase(0xFB)]
    [TestCase(0xF3)]
    [TestCase(0xF2)]
    public void Inspect_WithMp3SyncBytes_ReturnsMp3(byte secondByte)
    {
        var syncBytes = new byte[] { 0xFF, secondByte, 0x90, 0x44 };
        using var stream = new MemoryStream(syncBytes);
        var result = _inspector.Inspect(stream, "audio_clip");

        Assert.That(result.ContainerFormat, Is.EqualTo("MP3"));
        Assert.That(result.AudioCodec, Is.EqualTo("MP3"));
    }

    [Test]
    public void Inspect_WithRiffAviHeader_ReturnsAvi()
    {
        var aviBytes = new byte[12];
        aviBytes[0] = (byte)'R';
        aviBytes[1] = (byte)'I';
        aviBytes[2] = (byte)'F';
        aviBytes[3] = (byte)'F';
        aviBytes[8] = (byte)'A';
        aviBytes[9] = (byte)'V';
        aviBytes[10] = (byte)'I';
        aviBytes[11] = (byte)' ';

        using var stream = new MemoryStream(aviBytes);
        var result = _inspector.Inspect(stream, "clip_without_ext");

        Assert.That(result.ContainerFormat, Is.EqualTo("AVI"));
    }

    [Test]
    public void Inspect_WithRiffWaveHeader_ReturnsWav()
    {
        var waveBytes = new byte[12];
        waveBytes[0] = (byte)'R';
        waveBytes[1] = (byte)'I';
        waveBytes[2] = (byte)'F';
        waveBytes[3] = (byte)'F';
        waveBytes[8] = (byte)'W';
        waveBytes[9] = (byte)'A';
        waveBytes[10] = (byte)'V';
        waveBytes[11] = (byte)'E';

        using var stream = new MemoryStream(waveBytes);
        var result = _inspector.Inspect(stream, "audio_clip");

        Assert.That(result.ContainerFormat, Is.EqualTo("WAV"));
    }

    [Test]
    public void Inspect_WithOggHeader_ReturnsOgg()
    {
        var oggBytes = new byte[] { (byte)'O', (byte)'g', (byte)'g', (byte)'S', 0x00, 0x02 };
        using var stream = new MemoryStream(oggBytes);
        var result = _inspector.Inspect(stream, "soundtrack");

        Assert.That(result.ContainerFormat, Is.EqualTo("Ogg"));
    }

    [Test]
    public void Inspect_WithUnknownOrEmptyStream_FallsBackToInspectFileName()
    {
        var nullResult = _inspector.Inspect(null, "Movie.1080p.mkv");
        Assert.That(nullResult.ContainerFormat, Is.EqualTo("Matroska"));
        Assert.That(nullResult.Resolution, Is.EqualTo("1080p"));

        using var emptyStream = new MemoryStream();
        var emptyResult = _inspector.Inspect(emptyStream, "Movie.720p.mp4");
        Assert.That(emptyResult.ContainerFormat, Is.EqualTo("MPEG-4"));
        Assert.That(emptyResult.Resolution, Is.EqualTo("720p"));

        using var unknownStream = new MemoryStream(new byte[] { 0x00, 0x01, 0x02, 0x03 });
        var unknownResult = _inspector.Inspect(unknownStream, "Movie.2160p.x265.mkv");
        Assert.That(unknownResult.ContainerFormat, Is.EqualTo("Matroska"));
        Assert.That(unknownResult.Resolution, Is.EqualTo("2160p"));
        Assert.That(unknownResult.VideoCodec, Is.EqualTo("HEVC"));
    }

    [Test]
    public void InspectFile_WhenFfprobeFails_FallsBackToBinaryStreamInspection()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var header = new byte[16];
            header[0] = 0;
            header[1] = 0;
            header[2] = 0;
            header[3] = 16;
            header[4] = (byte)'f';
            header[5] = (byte)'t';
            header[6] = (byte)'y';
            header[7] = (byte)'p';
            header[8] = (byte)'i';
            header[9] = (byte)'s';
            header[10] = (byte)'o';
            header[11] = (byte)'m';
            File.WriteAllBytes(tempFile, header);

            var result = _inspector.InspectFile(tempFile);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ContainerFormat, Is.EqualTo("MPEG-4"));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
