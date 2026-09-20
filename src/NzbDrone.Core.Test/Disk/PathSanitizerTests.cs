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
    [TestCase("name\0with\u001Fcontrol.txt", "namewithcontrol.txt")]
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

    [TestCase("Season 1/Episode 01.mkv", "Season 1/Episode 01.mkv")]
    [TestCase("/Season 1/Episode 01.mkv", "Season 1/Episode 01.mkv")]
    [TestCase("///Season 1///Episode 01.mkv", "Season 1/Episode 01.mkv")]
    [TestCase("Season 1/../Season 2/Episode 01.mkv", "Season 2/Episode 01.mkv")]
    [TestCase("../../outside.mkv", "outside.mkv")]
    [TestCase("..", "")]
    [TestCase("/..", "")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    public void SanitizeRelativePath_strips_leading_slashes_and_traversal(string input, string expected)
    {
        Assert.That(PathSanitizer.SanitizeRelativePath(input), Is.EqualTo(expected));
    }

    [Test]
    public void IsPathUnderRoot_allows_paths_inside_root()
    {
        var root = "/downloads/MyTorrent";
        Assert.That(PathSanitizer.IsPathUnderRoot(root, "Season 1/Episode 01.mkv"), Is.True);
        Assert.That(PathSanitizer.IsPathUnderRoot(root, "file.mkv"), Is.True);
    }

    [Test]
    public void IsPathUnderRoot_rejects_paths_with_traversal()
    {
        var root = "/downloads/MyTorrent";
        Assert.That(PathSanitizer.IsPathUnderRoot(root, "../outside.mkv"), Is.False);
        Assert.That(PathSanitizer.IsPathUnderRoot(root, "../../etc/passwd"), Is.False);
        Assert.That(PathSanitizer.IsPathUnderRoot(root, "sub/../../outside.mkv"), Is.False);
    }
}
