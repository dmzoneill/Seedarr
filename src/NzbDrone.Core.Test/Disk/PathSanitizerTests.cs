using NUnit.Framework;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Test.Disk;

[TestFixture]
public class PathSanitizerTests
{
    [TestCase("../")]
    [TestCase("..\\")]
    [TestCase("../foo")]
    [TestCase("..\\foo")]
    [TestCase("foo/../bar")]
    [TestCase("foo\\..\\bar")]
    [TestCase("/foo/bar/..")]
    [TestCase("..")]
    [TestCase("./foo")]
    [TestCase("foo/./bar")]
    public void ContainsPathTraversal_detects_traversal_elements(string path)
    {
        Assert.That(PathSanitizer.ContainsPathTraversal(path), Is.True);
        Assert.That(PathSanitizer.IsValidPath(path), Is.False);
    }

    [TestCase("con")]
    [TestCase("aux")]
    [TestCase("nul.mkv")]
    [TestCase("com1.txt")]
    [TestCase("prn")]
    [TestCase("CON")]
    [TestCase("Aux")]
    [TestCase("NUL.MKV")]
    [TestCase("com9.dat")]
    [TestCase("lpt1")]
    [TestCase("lpt5.log")]
    public void IsValidFileName_rejects_windows_reserved_names(string name)
    {
        Assert.That(PathSanitizer.IsWindowsReservedName(name), Is.True);
        Assert.That(PathSanitizer.IsValidFileName(name), Is.False);
    }

    [TestCase("console.txt")]
    [TestCase("auxiliary.mkv")]
    [TestCase("com10.txt")]
    [TestCase("printer.log")]
    [TestCase("normal_file.dat")]
    [TestCase("My Movie (2024).mkv")]
    public void IsValidFileName_accepts_valid_names(string name)
    {
        Assert.That(PathSanitizer.IsWindowsReservedName(name), Is.False);
        Assert.That(PathSanitizer.IsValidFileName(name), Is.True);
    }

    [TestCase("file.txt.", false)]
    [TestCase("file.txt ", false)]
    [TestCase("folder. ", false)]
    [TestCase("folder... ", false)]
    [TestCase("valid.txt", true)]
    public void IsValidFileName_checks_trailing_period_and_space(string name, bool expected)
    {
        Assert.That(PathSanitizer.IsValidFileName(name), Is.EqualTo(expected));
    }

    [TestCase("file.txt.", "file.txt")]
    [TestCase("file.txt ", "file.txt")]
    [TestCase("file.txt. ", "file.txt")]
    [TestCase("file.txt...   ", "file.txt")]
    [TestCase("folder. ", "folder")]
    [TestCase("folder... ", "folder")]
    public void SanitizeFileName_strips_trailing_periods_and_spaces(string input, string expected)
    {
        Assert.That(PathSanitizer.SanitizeFileName(input), Is.EqualTo(expected));
    }

    [TestCase("con", "_con")]
    [TestCase("aux", "_aux")]
    [TestCase("nul.mkv", "_nul.mkv")]
    [TestCase("com1.txt", "_com1.txt")]
    [TestCase("prn", "_prn")]
    public void SanitizeFileName_prefixes_windows_reserved_names(string input, string expected)
    {
        Assert.That(PathSanitizer.SanitizeFileName(input), Is.EqualTo(expected));
    }

    [TestCase("file<name>:\"test\"|path?*.mkv", "filenametestpath.mkv")]
    [TestCase("name\x00with\x1Fcontrol.txt", "namewithcontrol.txt")]
    public void SanitizeFileName_strips_illegal_and_control_characters(string input, string expected)
    {
        Assert.That(PathSanitizer.SanitizeFileName(input), Is.EqualTo(expected));
    }

    [TestCase("valid/subfolder/file.mkv", true)]
    [TestCase("/absolute/path/file.mkv", true)]
    [TestCase("invalid/../traversal/file.mkv", false)]
    [TestCase("invalid/con/file.mkv", false)]
    [TestCase("invalid/folder./file.mkv", false)]
    [TestCase("invalid/fold<er>/file.mkv", false)]
    public void IsValidPath_validates_path_segments(string path, bool expected)
    {
        Assert.That(PathSanitizer.IsValidPath(path), Is.EqualTo(expected));
    }
}
