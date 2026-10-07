using System;
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
    public void GetLongestMatchingMountPoint_matches_mount_when_path_casing_differs()
    {
        var mounts = new[] { "/", "/mnt/nas" };

        Assert.That(
            DiskProvider.GetLongestMatchingMountPoint("/MNT/nas/torrents/file.part", mounts),
            Is.EqualTo("/mnt/nas"));
        Assert.That(
            DiskProvider.GetLongestMatchingMountPoint("/MNT/nas", mounts),
            Is.EqualTo("/mnt/nas"));
    }

    [Test]
    public void GetLongestMatchingMountPoint_prefers_windows_volume_mount_over_drive_letter()
    {
        var mounts = new[] { "C:\\", "C:\\Media\\Torrents" };

        Assert.That(
            DiskProvider.GetLongestMatchingMountPoint(@"C:\Media\Torrents\file.part", mounts),
            Is.EqualTo(@"C:\Media\Torrents"));
        Assert.That(
            DiskProvider.GetLongestMatchingMountPoint(@"C:\Users\file.part", mounts),
            Is.EqualTo("C:"));
    }

    [Test]
    public void GetAvailableFreeSpace_uses_longest_drive_prefix_from_drives_provider()
    {
        var rootDrive = new DriveInfo("/");
        var mediaDrive = new DriveInfo("/mnt/media");
        var subject = new DiskProvider
        {
            DrivesProvider = () => new[] { rootDrive, mediaDrive },
        };

        var nestedPathFree = subject.GetAvailableFreeSpace("/mnt/media/shows/season1/file.mkv");
        var rootPathFree = subject.GetAvailableFreeSpace("/var/tmp/seedarr_free_space_test");

        Assert.That(nestedPathFree, Is.EqualTo(mediaDrive.AvailableFreeSpace));
        Assert.That(rootPathFree, Is.EqualTo(rootDrive.AvailableFreeSpace));
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

    [Test]
    public void GetAvailableFreeSpace_returns_null_when_drives_provider_throws()
    {
        var subject = new DiskProvider
        {
            DrivesProvider = () => throw new IOException("drive not ready"),
        };

        Assert.That(subject.GetAvailableFreeSpace("/mnt/nas/shows/file.mkv"), Is.Null);
    }

    [Test]
    public void CheckFolderWritable_returns_false_for_blank_path()
    {
        var subject = new DiskProvider();

        Assert.That(subject.CheckFolderWritable("   "), Is.False);
        Assert.That(subject.CheckFolderWritable(null), Is.False);
    }

    [TestCase("../outside")]
    [TestCase("foo/../bar")]
    public void CheckFolderWritable_returns_false_for_path_traversal(string path)
    {
        var subject = new DiskProvider();

        Assert.That(subject.CheckFolderWritable(path), Is.False);
    }

    [Test]
    public void CheckFolderWritable_returns_false_for_blocked_system_path_without_creating_directory()
    {
        var blockedPath = OperatingSystem.IsWindows() ? @"C:\Windows\seedarr_perm_test" : "/etc/seedarr_perm_test";
        var subject = new DiskProvider();

        Assert.That(subject.CheckFolderWritable(blockedPath), Is.False);
        Assert.That(Directory.Exists(blockedPath), Is.False);
    }

    [Test]
    public void CheckFolderWritable_returns_true_for_writable_temp_subdirectory()
    {
        var subject = new DiskProvider();
        var target = Path.Combine(Path.GetTempPath(), "seedarr_perm_test_" + Guid.NewGuid().ToString("N"));

        try
        {
            Assert.That(subject.CheckFolderWritable(target), Is.True);
            Assert.That(Directory.Exists(target), Is.True);
        }
        finally
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
        }
    }
}
