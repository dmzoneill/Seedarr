using System.IO;
using NUnit.Framework;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.Test.Disk;

[TestFixture]
public class DiskProviderTest
{
    [Test]
    public void GetLongestMatchingMountPoint_prefers_longest_prefix_over_root()
    {
        var mounts = new[] { "/", "/mnt/media", "/downloads" };

        Assert.That(
            DiskProvider.GetLongestMatchingMountPoint("/mnt/media/shows/season1", mounts),
            Is.EqualTo("/mnt/media"));
        Assert.That(
            DiskProvider.GetLongestMatchingMountPoint("/downloads/incomplete/file.part", mounts),
            Is.EqualTo("/downloads"));
    }

    [Test]
    public void GetLongestMatchingMountPoint_does_not_match_partial_directory_name()
    {
        var mounts = new[] { "/", "/mnt/media" };

        Assert.That(
            DiskProvider.GetLongestMatchingMountPoint("/mnt/mediaextra/file", mounts),
            Is.EqualTo("/"));
    }

    [Test]
    public void GetLongestMatchingMountPoint_matches_exact_mount_point()
    {
        var mounts = new[] { "/", "/mnt/data" };

        Assert.That(DiskProvider.GetLongestMatchingMountPoint("/mnt/data", mounts), Is.EqualTo("/mnt/data"));
    }

    [Test]
    public void GetAvailableFreeSpace_returns_non_negative_for_temp_path()
    {
        var subject = new DiskProvider();
        var freeSpace = subject.GetAvailableFreeSpace(Path.GetTempPath());

        Assert.That(freeSpace, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void GetAvailableFreeSpace_returns_zero_for_blank_path()
    {
        var subject = new DiskProvider();

        Assert.That(subject.GetAvailableFreeSpace("   "), Is.EqualTo(0));
        Assert.That(subject.GetAvailableFreeSpace(null), Is.EqualTo(0));
    }

}
