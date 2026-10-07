using System;
using System.Collections.Generic;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Torrents;

[TestFixture]
public class DiskAllocationServiceTest
{
    private IDiskSpaceService _diskSpaceService;
    private IConfigService _configService;
    private DiskAllocationService _subject;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _diskSpaceService = Substitute.For<IDiskSpaceService>();
        _configService = Substitute.For<IConfigService>();
        _configService.PreallocationMode.Returns(PreallocationMode.Sparse);

        _tempDir = Path.Combine(Path.GetTempPath(), "DiskAllocationTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _subject = new DiskAllocationService(_diskSpaceService, _configService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignored in test cleanup
            }
        }
    }

    [Test]
    public void PreallocateFiles_should_throw_when_disk_space_is_insufficient()
    {
        _configService.PreallocationMode.Returns(PreallocationMode.Sparse);
        _diskSpaceService.GetDiskSpaceForPath(Arg.Any<string>())
            .Returns(new DiskSpaceInfo { Path = _tempDir, FreeSpace = 1000 });

        var torrent = new Torrent
        {
            Name = "TestTorrent",
            TotalSize = 5000,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "file.bin", Size = 5000 }
        };

        var ex = Assert.Throws<InsufficientDiskSpaceException>(() =>
            _subject.PreallocateFiles(torrent, files));

        Assert.That(ex.Message, Does.Contain("Insufficient disk space"));
    }

    [Test]
    public void PreallocateFiles_should_allocate_sparse_files_with_correct_length()
    {
        _configService.PreallocationMode.Returns(PreallocationMode.Sparse);
        _diskSpaceService.GetDiskSpaceForPath(Arg.Any<string>())
            .Returns(new DiskSpaceInfo { Path = _tempDir, FreeSpace = 100000 });

        var torrent = new Torrent
        {
            Name = "SparseTorrent",
            TotalSize = 3072,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "sparse1.dat", Size = 1024 },
            new TorrentFile { Path = Path.Combine("subdir", "sparse2.dat"), Size = 2048 }
        };

        _subject.PreallocateFiles(torrent, files);

        var file1 = Path.Combine(_tempDir, "sparse1.dat");
        var file2 = Path.Combine(_tempDir, "subdir", "sparse2.dat");

        Assert.That(File.Exists(file1), Is.True);
        Assert.That(new FileInfo(file1).Length, Is.EqualTo(1024));

        Assert.That(File.Exists(file2), Is.True);
        Assert.That(new FileInfo(file2).Length, Is.EqualTo(2048));
    }

    [Test]
    public void PreallocateFiles_should_allocate_full_files_with_correct_length()
    {
        _configService.PreallocationMode.Returns(PreallocationMode.Full);
        _diskSpaceService.GetDiskSpaceForPath(Arg.Any<string>())
            .Returns(new DiskSpaceInfo { Path = _tempDir, FreeSpace = 100000 });

        var torrent = new Torrent
        {
            Name = "FullTorrent",
            TotalSize = 2048,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "full.dat", Size = 2048 }
        };

        _subject.PreallocateFiles(torrent, files);

        var file = Path.Combine(_tempDir, "full.dat");
        Assert.That(File.Exists(file), Is.True);
        Assert.That(new FileInfo(file).Length, Is.EqualTo(2048));
    }

    [Test]
    public void PreallocateFiles_should_bypass_when_mode_is_none()
    {
        _configService.PreallocationMode.Returns(PreallocationMode.None);

        var torrent = new Torrent
        {
            Name = "BypassedTorrent",
            TotalSize = 2048,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "bypassed.dat", Size = 2048 }
        };

        _subject.PreallocateFiles(torrent, files);

        var file = Path.Combine(_tempDir, "bypassed.dat");
        Assert.That(File.Exists(file), Is.False);
        _diskSpaceService.DidNotReceiveWithAnyArgs().GetDiskSpaceForPath(default);
    }

    [Test]
    public void PreallocateFiles_should_exclude_padding_from_free_space_check()
    {
        _configService.PreallocationMode.Returns(PreallocationMode.Sparse);
        _diskSpaceService.GetDiskSpaceForPath(Arg.Any<string>())
            .Returns(new DiskSpaceInfo { Path = _tempDir, FreeSpace = 2000 });

        var torrent = new Torrent
        {
            Name = "PaddingSpaceTorrent",
            TotalSize = 10000,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "content.dat", Size = 2000 },
            new TorrentFile { Path = ".pad/8000", Size = 8000, IsPaddingFile = true }
        };

        Assert.DoesNotThrow(() => _subject.PreallocateFiles(torrent, files));
        Assert.That(File.Exists(Path.Combine(_tempDir, "content.dat")), Is.True);
    }

    [Test]
    public void PreallocateFiles_should_skip_bep47_padding_files()
    {
        _configService.PreallocationMode.Returns(PreallocationMode.Sparse);
        _diskSpaceService.GetDiskSpaceForPath(Arg.Any<string>())
            .Returns(new DiskSpaceInfo { Path = _tempDir, FreeSpace = 100000 });

        var torrent = new Torrent
        {
            Name = "PaddingTorrent",
            TotalSize = 3072,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "content.dat", Size = 1024 },
            new TorrentFile { Path = ".pad/2048", Size = 2048, IsPaddingFile = true }
        };

        _subject.PreallocateFiles(torrent, files);

        Assert.That(File.Exists(Path.Combine(_tempDir, "content.dat")), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempDir, ".pad", "2048")), Is.False);
    }

    [Test]
    public void PreallocateFiles_should_use_baseDirectory_override_when_provided()
    {
        _configService.PreallocationMode.Returns(PreallocationMode.Sparse);
        _diskSpaceService.GetDiskSpaceForPath(Arg.Any<string>())
            .Returns(new DiskSpaceInfo { Path = _tempDir, FreeSpace = 100000 });

        var customDir = Path.Combine(_tempDir, "custom");
        Directory.CreateDirectory(customDir);

        var torrent = new Torrent
        {
            Name = "CustomDirTorrent",
            TotalSize = 1024,
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "custom.dat", Size = 1024 }
        };

        _subject.PreallocateFiles(torrent, files, customDir);

        var file = Path.Combine(customDir, "custom.dat");
        Assert.That(File.Exists(file), Is.True);
        Assert.That(new FileInfo(file).Length, Is.EqualTo(1024));
    }

    [Test]
    public void PreallocateFiles_should_throw_ArgumentNullException_when_torrent_is_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _subject.PreallocateFiles(null, new List<TorrentFile>()));
    }

    [Test]
    public void PreallocateFiles_should_handle_empty_files_list_without_error()
    {
        _configService.PreallocationMode.Returns(PreallocationMode.Sparse);
        var torrent = new Torrent
        {
            Name = "EmptyTorrent",
            TotalSize = 0,
            SavePath = _tempDir
        };

        Assert.DoesNotThrow(() =>
            _subject.PreallocateFiles(torrent, new List<TorrentFile>()));
    }

    [Test]
    public void PreallocateFiles_should_allow_when_remaining_bytes_fit_within_free_space()
    {
        _configService.PreallocationMode.Returns(PreallocationMode.Sparse);
        _diskSpaceService.GetDiskSpaceForPath(Arg.Any<string>())
            .Returns(new DiskSpaceInfo { Path = _tempDir, FreeSpace = 1000 });

        var torrent = new Torrent
        {
            Name = "PartialTorrent",
            TotalSize = 5000,
            Downloaded = 4500, // 500 bytes remaining < 1000 free space
            SavePath = _tempDir
        };

        var files = new List<TorrentFile>
        {
            new TorrentFile { Path = "partial.dat", Size = 5000 }
        };

        Assert.DoesNotThrow(() =>
            _subject.PreallocateFiles(torrent, files));

        var file = Path.Combine(_tempDir, "partial.dat");
        Assert.That(File.Exists(file), Is.True);
        Assert.That(new FileInfo(file).Length, Is.EqualTo(5000));
    }

    [Test]
    public void ResolveFilePath_should_throw_SecurityException_on_path_traversal()
    {
        Assert.Throws<System.Security.SecurityException>(() =>
            DiskAllocationService.ResolveFilePath(_tempDir, "../../etc/passwd"));
    }

    [Test]
    public void ResolveFilePath_should_throw_SecurityException_on_rooted_path()
    {
        var rooted = OperatingSystem.IsWindows() ? "C:\\Windows\\win.ini" : "/etc/shadow";
        Assert.Throws<System.Security.SecurityException>(() =>
            DiskAllocationService.ResolveFilePath(_tempDir, rooted));
    }

    [Test]
    public void ResolveFilePath_should_resolve_safe_subpath()
    {
        var resolved = DiskAllocationService.ResolveFilePath(_tempDir, "sub/dir/safe.dat");
        var expected = Path.GetFullPath(Path.Combine(_tempDir, "sub", "dir", "safe.dat"));
        Assert.That(resolved, Is.EqualTo(expected));
    }
}
