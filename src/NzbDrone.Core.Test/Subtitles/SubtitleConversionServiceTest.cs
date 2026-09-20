using System.Text;
using NUnit.Framework;
using NzbDrone.Core.Subtitles;

namespace NzbDrone.Core.Test.Subtitles;

[TestFixture]
public class SubtitleConversionServiceTest
{
    private SubtitleConversionService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new SubtitleConversionService();
    }

    [Test]
    public void ConvertToWebVtt_SubRipWithCommaTimecodes_NormalizesToDotsAndAddsHeader()
    {
        var srt = "1\n00:01:23,456 --> 00:01:25,789\nHello, world!\n";
        var result = _service.ConvertToWebVtt(srt);

        Assert.That(result, Does.StartWith("WEBVTT\n\n"));
        Assert.That(result, Does.Contain("00:01:23.456 --> 00:01:25.789"));
        Assert.That(result, Does.Contain("Hello, world!"));
    }

    [Test]
    public void ConvertToWebVtt_SingleDigitHours_PadsHoursToTwoDigits()
    {
        var srt = "1\n1:02:03,400 --> 1:02:05,600\nHour test\n";
        var result = _service.ConvertToWebVtt(srt);

        Assert.That(result, Does.Contain("01:02:03.400 --> 01:02:05.600"));
    }

    [Test]
    public void ConvertToWebVtt_MissingHours_PadsHoursToZero()
    {
        var srt = "1\n02:03,400 --> 02:05,600\nNo hours test\n";
        var result = _service.ConvertToWebVtt(srt);

        Assert.That(result, Does.Contain("00:02:03.400 --> 00:02:05.600"));
    }

    [Test]
    public void ConvertToWebVtt_TwoDigitMilliseconds_PadsToThreeDigits()
    {
        var srt = "1\n00:01:23,45 --> 00:01:25,7\nShort ms test\n";
        var result = _service.ConvertToWebVtt(srt);

        Assert.That(result, Does.Contain("00:01:23.450 --> 00:01:25.700"));
    }

    [Test]
    public void ConvertToWebVtt_CueIdentifiers_CleanedToSequentialNumbers()
    {
        var srt = "45\n00:00:01,000 --> 00:00:02,000\nFirst cue\n\n99\n00:00:03,000 --> 00:00:04,000\nSecond cue\n";
        var result = _service.ConvertToWebVtt(srt);

        var lines = result.Split('\n');
        Assert.That(lines[2], Is.EqualTo("1"));
        Assert.That(lines[3], Is.EqualTo("00:00:01.000 --> 00:00:02.000"));
        Assert.That(lines[6], Is.EqualTo("2"));
        Assert.That(lines[7], Is.EqualTo("00:00:03.000 --> 00:00:04.000"));
    }

    [Test]
    public void ConvertToWebVtt_HtmlFontTagsAndAssTags_StrippedFromCueText()
    {
        var srt = "1\n00:00:01,000 --> 00:00:02,000\n<font color=\"#ff0000\">{\\an8}Red Top Subtitle</font>\n";
        var result = _service.ConvertToWebVtt(srt);

        Assert.That(result, Does.Not.Contain("<font"));
        Assert.That(result, Does.Not.Contain("</font>"));
        Assert.That(result, Does.Not.Contain("{\\an8}"));
        Assert.That(result, Does.Contain("Red Top Subtitle"));
    }

    [Test]
    public void ConvertToWebVtt_AlreadyWebVtt_PreservesHeaderWithoutDuplication()
    {
        var vtt = "WEBVTT\n\n1\n00:00:01.000 --> 00:00:02.000\nAlready WebVTT\n";
        var result = _service.ConvertToWebVtt(vtt);

        Assert.That(result, Does.StartWith("WEBVTT\n\n"));
        var occurrences = (result.Length - result.Replace("WEBVTT", "").Length) / "WEBVTT".Length;
        Assert.That(occurrences, Is.EqualTo(1));
        Assert.That(result, Does.Contain("Already WebVTT"));
    }

    [Test]
    public void ConvertToWebVtt_EmptyOrWhitespace_ReturnsHeaderOnly()
    {
        var result = _service.ConvertToWebVtt("   \n\r  ");
        Assert.That(result, Is.EqualTo("WEBVTT\n\n"));
    }

    [Test]
    public void ConvertToWebVtt_MicroDvdFormat_ConvertsToTimecodes()
    {
        var microDvd = "{24}{48}MicroDVD subtitle|line two\n";
        var result = _service.ConvertToWebVtt(microDvd, "sub");

        Assert.That(result, Does.StartWith("WEBVTT\n\n"));
        Assert.That(result, Does.Contain("-->"));
        Assert.That(result, Does.Contain("MicroDVD subtitle\nline two"));
    }

    [Test]
    public void ConvertToWebVtt_RawBytesWithEncoding_DecodesAndConverts()
    {
        var srt = "1\n00:00:01,000 --> 00:00:02,000\nBonjour le monde\n";
        var bytes = Encoding.UTF8.GetBytes(srt);
        var result = _service.ConvertToWebVtt(bytes, "srt");

        Assert.That(result, Does.StartWith("WEBVTT\n\n"));
        Assert.That(result, Does.Contain("Bonjour le monde"));
    }
}
