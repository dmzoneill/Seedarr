using System.Linq;
using System.Text;
using NUnit.Framework;
using NzbDrone.Core.Subtitles;

namespace NzbDrone.Core.Test.Subtitles;

[TestFixture]
public class SubtitleEncodingDetectorTest
{
    private SubtitleEncodingDetector _detector;

    [SetUp]
    public void SetUp()
    {
        _detector = new SubtitleEncodingDetector();
    }

    [Test]
    public void DetectEncoding_Utf8WithBom_ReturnsUtf8()
    {
        var text = "Hello world";
        var textBytes = Encoding.UTF8.GetBytes(text);
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(textBytes).ToArray();

        var encoding = _detector.DetectEncoding(bytes);
        Assert.That(encoding.WebName, Is.EqualTo("utf-8"));
    }

    [Test]
    public void DetectEncoding_Utf8WithoutBom_ReturnsUtf8()
    {
        var text = "Café au lait — très bien!";
        var bytes = Encoding.UTF8.GetBytes(text);

        var encoding = _detector.DetectEncoding(bytes);
        Assert.That(encoding.WebName, Is.EqualTo("utf-8"));
    }

    [Test]
    public void DetectEncoding_Utf16LEWithBom_ReturnsUnicode()
    {
        var text = "Hello in UTF-16 LE";
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();

        var encoding = _detector.DetectEncoding(bytes);
        Assert.That(encoding.WebName, Is.EqualTo("utf-16"));
    }

    [Test]
    public void DetectEncoding_Utf16BEWithBom_ReturnsBigEndianUnicode()
    {
        var text = "Hello in UTF-16 BE";
        var bytes = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(text)).ToArray();

        var encoding = _detector.DetectEncoding(bytes);
        Assert.That(encoding.WebName, Is.EqualTo("utf-16BE"));
    }

    [Test]
    public void DetectEncoding_Windows1252_WithAccentedCharacters_Returns1252OrLatin1()
    {
        // 0x93 and 0x94 are Windows-1252 curved quotes “ ”
        // 0xE9 is é
        var bytes = new byte[] { 0x93, 0x43, 0x61, 0x66, 0xE9, 0x94 };

        var encoding = _detector.DetectEncoding(bytes);
        Assert.That(encoding.WebName, Is.AnyOf("windows-1252", "iso-8859-1"));
    }

    [Test]
    public void DecodeToUtf8_Windows1252_AccentedCharacters_DecodesCorrectly()
    {
        // "Café" in Windows-1252 / ISO-8859-1: C=0x43, a=0x61, f=0x66, é=0xE9
        var bytes = new byte[] { 0x43, 0x61, 0x66, 0xE9 };
        var decoded = _detector.DecodeToUtf8(bytes);

        Assert.That(decoded, Is.EqualTo("Café"));
    }

    [Test]
    public void DecodeToUtf8_Utf8WithBom_StripsBomCharacter()
    {
        var text = "No BOM in decoded text";
        var textBytes = Encoding.UTF8.GetBytes(text);
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(textBytes).ToArray();

        var decoded = _detector.DecodeToUtf8(bytes);
        Assert.That(decoded, Is.EqualTo(text));
        Assert.That(decoded[0], Is.Not.EqualTo('\uFEFF'));
    }

    [Test]
    public void DecodeToUtf8_Utf16LE_DecodesCorrectly()
    {
        var text = "Subtitle in UTF-16";
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();

        var decoded = _detector.DecodeToUtf8(bytes);
        Assert.That(decoded, Is.EqualTo(text));
    }
}
