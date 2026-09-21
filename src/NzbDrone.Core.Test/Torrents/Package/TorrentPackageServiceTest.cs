using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Packages;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Torrents.Package;
using Seedarr.Api.V1.Packages;

namespace NzbDrone.Core.Test.Torrents.Package;

[TestFixture]
public class TorrentPackageServiceTest
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private IFastResumeService _fastResumeService;
    private IFastResumeBencodeSerializer _bencodeSerializer;
    private ITagService _tagService;
    private ITrackerEntryService _trackerEntryService;
    private IAppFolderInfo _appFolderInfo;
    private ISyntheticMetadataGenerator _syntheticMetadataGenerator;
    private ITorrentImportService _torrentImportService;
    private IDiskProvider _diskProvider;

    private TorrentPackageService _service;
    private string _tempDir;
    private string _sandboxDir;
    private string _appDataDir;
    private Torrent _testTorrent;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_torrent_pkg_" + Guid.NewGuid().ToString("N"));
        _sandboxDir = Path.Combine(_tempDir, "sandbox");
        _appDataDir = Path.Combine(_tempDir, "appdata");
        Directory.CreateDirectory(_sandboxDir);
        Directory.CreateDirectory(_appDataDir);

        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _fastResumeService = Substitute.For<IFastResumeService>();
        _bencodeSerializer = new FastResumeBencodeSerializer();
        _tagService = Substitute.For<ITagService>();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _syntheticMetadataGenerator = new SyntheticMetadataGenerator();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _diskProvider = Substitute.For<IDiskProvider>();

        _appFolderInfo.AppDataFolder.Returns(_appDataDir);
        _diskProvider.GetAvailableFreeSpace(Arg.Any<string>()).Returns(100L * 1024 * 1024 * 1024);

        _testTorrent = new Torrent
        {
            Id = 1,
            Name = "Ubuntu 24.04 Desktop",
            InfoHash = "4a5b6c7d8e9f0123456789abcdef0123456789ab",
            Category = "Linux",
            TagIds = new List<int> { 10, 20 },
            TotalSize = 131072,
            PieceCount = 2,
            PieceLength = 65536,
            IsPrivate = false,
            SavePath = _tempDir,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Uploaded = 500000,
            Downloaded = 131072
        };

        _torrentService.Get(1).Returns(_testTorrent);
        _tagService.GetLabelsForTagIds(Arg.Any<IEnumerable<int>>()).Returns(new List<string> { "linux", "iso" });

        var trackers = new List<TrackerEntry>
        {
            new TrackerEntry
            {
                Id = 1,
                TorrentId = 1,
                Url = "http://tracker.ubuntu.com:6969/announce",
                Tier = 0,
                Status = TrackerStatus.Working,
                Enabled = true,
                Seeders = 100,
                Leechers = 10,
                TotalAnnounces = 5,
                SuccessfulAnnounces = 5
            }
        };
        _trackerEntryService.GetByTorrentId(1).Returns(trackers);

        _service = new TorrentPackageService(
            _torrentService,
            torrentFileService: _torrentFileService,
            fastResumeService: _fastResumeService,
            bencodeSerializer: _bencodeSerializer,
            tagService: _tagService,
            trackerEntryService: _trackerEntryService,
            appFolderInfo: _appFolderInfo,
            syntheticMetadataGenerator: _syntheticMetadataGenerator,
            torrentImportService: _torrentImportService,
            diskProvider: _diskProvider);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
        }
    }

    [Test]
    public async Task ExportPackageAsync_BundlesMetainfoAndFastResumeState()
    {
        using var outputStream = new MemoryStream();

        await _service.ExportPackageAsync(outputStream, new[] { 1 }, includePayload: false);

        outputStream.Position = 0;
        await using var gzipStream = new GZipStream(outputStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        var entries = new Dictionary<string, byte[]>();
        while (await tarReader.GetNextEntryAsync() is { } entry)
        {
            if (entry.DataStream != null)
            {
                using var ms = new MemoryStream();
                await entry.DataStream.CopyToAsync(ms);
                entries[entry.Name] = ms.ToArray();
            }
            else
            {
                entries[entry.Name] = Array.Empty<byte>();
            }
        }

        Assert.That(entries.ContainsKey("manifest.json"), Is.True, "manifest.json should be present");
        Assert.That(entries.ContainsKey("metainfo/4a5b6c7d8e9f0123456789abcdef0123456789ab.torrent"), Is.True, "metainfo .torrent should be present");
        Assert.That(entries.ContainsKey("fastresume/4a5b6c7d8e9f0123456789abcdef0123456789ab.fastresume"), Is.True, "fastresume should be present");

        // Verify manifest contains tracker histories and metadata
        var manifestJson = Encoding.UTF8.GetString(entries["manifest.json"]);
        var manifest = manifestJson.FromJson<PackageManifest>();

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest.SchemaVersion, Is.EqualTo(1));
        Assert.That(manifest.Client, Is.EqualTo("Seedarr"));
        Assert.That(manifest.Torrents, Has.Count.EqualTo(1));

        var t = manifest.Torrents[0];
        Assert.That(t.Name, Is.EqualTo("Ubuntu 24.04 Desktop"));
        Assert.That(t.InfoHash, Is.EqualTo("4a5b6c7d8e9f0123456789abcdef0123456789ab"));
        Assert.That(t.Category, Is.EqualTo("Linux"));
        Assert.That(t.Tags, Does.Contain("linux"));
        Assert.That(t.Trackers, Has.Count.EqualTo(1));
        Assert.That(t.Trackers[0].Url, Is.EqualTo("http://tracker.ubuntu.com:6969/announce"));
        Assert.That(t.Trackers[0].Status, Is.EqualTo("Working"));

        // Verify fastresume bitfield and client compatibility
        var resumeBytes = entries["fastresume/4a5b6c7d8e9f0123456789abcdef0123456789ab.fastresume"];
        Assert.That(_bencodeSerializer.IsBencode(resumeBytes), Is.True);
        var resumeData = _bencodeSerializer.Deserialize(resumeBytes);
        Assert.That(resumeData.InfoHash, Is.EqualTo("4a5b6c7d8e9f0123456789abcdef0123456789ab"));
        Assert.That(resumeData.Bitfield, Is.Not.Null);
        Assert.That(resumeData.Bitfield.All(b => b), Is.True, "Seeding torrent fastresume bitfield must have all pieces set to true");
    }

    [Test]
    public async Task ImportPackageAsync_ExtractsAndIngestsValidPackagesCleanly()
    {
        const string infoHash = "1234567890abcdef1234567890abcdef12345678";
        _torrentService.ExistsByInfoHash(infoHash).Returns(false);

        var manifestBytes = CreateValidManifestBytes(infoHash, "Valid Torrent", "Linux");
        var torrentBytes = Encoding.UTF8.GetBytes("d8:announce11:http://test4:infod4:name4:teste");
        var fastresumeBytes = Encoding.UTF8.GetBytes("d8:info_hash20:1234567890abcdef1234e");

        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", manifestBytes);
            AddFileEntry(tar, $"metainfo/{infoHash}.torrent", torrentBytes);
            AddFileEntry(tar, $"fastresume/{infoHash}.fastresume", fastresumeBytes);
        });

        var createdTorrent = new Torrent
        {
            Id = 42,
            Name = "Valid Torrent",
            InfoHash = infoHash,
            Category = "Linux",
            TotalSize = 1024
        };

        _torrentImportService.ImportFromFile(Arg.Any<Stream>(), Arg.Any<string>())
            .Returns(createdTorrent);

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions
        {
            TargetRootDir = _sandboxDir,
            RestoreTorrents = true
        };

        var result = await _service.ImportPackageAsync(archiveStream, options);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.ImportedTorrentsCount, Is.EqualTo(1));
        Assert.That(result.Torrents, Has.Count.EqualTo(1));
        Assert.That(result.Torrents[0].Id, Is.EqualTo(42));
        Assert.That(result.Torrents[0].InfoHash, Is.EqualTo(infoHash));
        Assert.That(result.Torrents[0].IsDuplicate, Is.False);

        // Verify files extracted into appdata
        var restoredTorrent = Path.Combine(_appDataDir, "torrents", $"{infoHash}.torrent");
        var restoredFastResume = Path.Combine(_appDataDir, "fastresume", $"{infoHash}.fastresume");
        Assert.That(File.Exists(restoredTorrent), Is.True);
        Assert.That(File.Exists(restoredFastResume), Is.True);
    }

    [Test]
    public void ImportPackageAsync_ZipSlipParentDirectoryTraversal_ThrowsSecurityException()
    {
        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddFileEntry(tar, "../../malicious.txt", Encoding.UTF8.GetBytes("MALICIOUS PAYLOAD"));
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions { TargetRootDir = _sandboxDir };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("Zip-Slip"));
        Assert.That(File.Exists(Path.Combine(_tempDir, "malicious.txt")), Is.False);
    }

    [Test]
    public void ImportPackageAsync_ZipSlipAbsoluteUnixPath_ThrowsSecurityException()
    {
        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddFileEntry(tar, "/etc/passwd", Encoding.UTF8.GetBytes("MALICIOUS ETC PASSWD"));
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions { TargetRootDir = _sandboxDir };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("Zip-Slip"));
    }

    [Test]
    public void ImportPackageAsync_ZipSlipWindowsDriveLetter_ThrowsSecurityException()
    {
        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddFileEntry(tar, "C:\\Windows\\System32\\calc.exe", Encoding.UTF8.GetBytes("MALICIOUS CALC"));
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions { TargetRootDir = _sandboxDir };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("Zip-Slip"));
    }

    [Test]
    public void ImportPackageAsync_SymbolicLink_ThrowsSecurityException()
    {
        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddSymlinkEntry(tar, "symlink_evil", "/etc/shadow");
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions { TargetRootDir = _sandboxDir };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("Symlink or hardlink rejected"));
    }

    [Test]
    public void ImportPackageAsync_HardLink_ThrowsSecurityException()
    {
        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddHardlinkEntry(tar, "hardlink_evil", "/etc/passwd");
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions { TargetRootDir = _sandboxDir };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("Symlink or hardlink rejected"));
    }

    [Test]
    public async Task ImportPackageAsync_DuplicateInfoHash_ChecksExistsBeforeExtractingAndHandlesCleanly()
    {
        const string duplicateHash = "1111222233334444555566667777888899990000";

        // Active torrent exists in library
        _torrentService.ExistsByInfoHash(duplicateHash).Returns(true);
        _torrentService.GetByInfoHash(duplicateHash).Returns(new Torrent
        {
            Id = 88,
            Name = "Existing Active Torrent",
            InfoHash = duplicateHash,
            Category = "Active"
        });

        var manifestBytes = CreateValidManifestBytes(duplicateHash, "Existing Active Torrent", "Active");
        var torrentBytes = Encoding.UTF8.GetBytes("d8:announce11:http://test4:infod4:name4:teste");
        var fastresumeBytes = Encoding.UTF8.GetBytes("d8:info_hash20:11112222333344445555e");
        var payloadBytes = Encoding.UTF8.GetBytes("SHOULD NOT BE EXTRACTED");

        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", manifestBytes);
            AddFileEntry(tar, $"metainfo/{duplicateHash}.torrent", torrentBytes);
            AddFileEntry(tar, $"fastresume/{duplicateHash}.fastresume", fastresumeBytes);
            AddFileEntry(tar, $"content/Existing Active Torrent/file.dat", payloadBytes);
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions
        {
            TargetRootDir = _sandboxDir,
            SkipDuplicates = true,
            RestoreTorrents = true
        };

        var result = await _service.ImportPackageAsync(archiveStream, options);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.ImportedTorrentsCount, Is.EqualTo(0));
        Assert.That(result.SkippedDuplicatesCount, Is.EqualTo(1));
        Assert.That(result.SkippedDuplicates, Does.Contain(duplicateHash));
        Assert.That(result.Torrents, Has.Count.EqualTo(1));
        Assert.That(result.Torrents[0].IsDuplicate, Is.True);
        Assert.That(result.Torrents[0].Id, Is.EqualTo(88));
        Assert.That(result.Message, Does.Contain("skipped duplicates"));

        // Verify ExistsByInfoHash was called to verify candidate before extraction
        _torrentService.Received().ExistsByInfoHash(duplicateHash);

        // Verify duplicate torrent was NOT imported again into TorrentImportService
        _torrentImportService.DidNotReceive().ImportFromFile(Arg.Any<Stream>(), Arg.Any<string>());

        // Verify duplicate files were NOT extracted to destination or Seedarr directories
        var extractedMetainfo = Path.Combine(_sandboxDir, "metainfo", $"{duplicateHash}.torrent");
        Assert.That(File.Exists(extractedMetainfo), Is.False, "Duplicate metainfo must not be extracted to sandbox");

        var extractedPayload = Path.Combine(_sandboxDir, "content", "Existing Active Torrent", "file.dat");
        Assert.That(File.Exists(extractedPayload), Is.False, "Duplicate payload file must not be extracted to sandbox");
    }

    [Test]
    public async Task ImportPackageAsync_MixedBatch_ImportsNewAndSkipsDuplicate()
    {
        const string newHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string dupHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        _torrentService.ExistsByInfoHash(newHash).Returns(false);
        _torrentService.ExistsByInfoHash(dupHash).Returns(true);
        _torrentService.GetByInfoHash(dupHash).Returns(new Torrent { Id = 10, Name = "Dup", InfoHash = dupHash });

        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            ExportedAt = DateTime.UtcNow,
            Client = "Seedarr",
            Torrents = new List<PackageTorrentItem>
            {
                new PackageTorrentItem { Id = 1, Name = "New Torrent", InfoHash = newHash, Category = "Cat" },
                new PackageTorrentItem { Id = 2, Name = "Dup Torrent", InfoHash = dupHash, Category = "Cat" }
            }
        };
        var manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());

        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", manifestBytes);
            AddFileEntry(tar, $"metainfo/{newHash}.torrent", Encoding.UTF8.GetBytes("new-torrent-bytes"));
            AddFileEntry(tar, $"metainfo/{dupHash}.torrent", Encoding.UTF8.GetBytes("dup-torrent-bytes"));
        });

        _torrentImportService.ImportFromFile(Arg.Any<Stream>(), Arg.Is<string>(s => s.Contains(newHash)))
            .Returns(new Torrent { Id = 99, Name = "New Torrent", InfoHash = newHash });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions
        {
            TargetRootDir = _sandboxDir,
            SkipDuplicates = true,
            RestoreTorrents = true
        };

        var result = await _service.ImportPackageAsync(archiveStream, options);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ImportedTorrentsCount, Is.EqualTo(1));
        Assert.That(result.SkippedDuplicatesCount, Is.EqualTo(1));
        Assert.That(result.SkippedDuplicates, Does.Contain(dupHash));

        var newSummary = result.Torrents.First(t => t.InfoHash == newHash);
        Assert.That(newSummary.IsDuplicate, Is.False);
        Assert.That(newSummary.Id, Is.EqualTo(99));

        var dupSummary = result.Torrents.First(t => t.InfoHash == dupHash);
        Assert.That(dupSummary.IsDuplicate, Is.True);
        Assert.That(dupSummary.Id, Is.EqualTo(10));
    }

    [Test]
    public async Task Controller_PostExport_StreamsPackageAndSetsHeaders()
    {
        var controller = new PackageController(_service, _torrentService);

        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var request = new PackageExportRequest
        {
            TorrentIds = new List<int> { 1 },
            IncludePayload = false
        };

        var actionResult = await controller.ExportPost(request);

        Assert.That(actionResult, Is.InstanceOf<EmptyResult>());
        Assert.That(httpContext.Response.ContentType, Is.EqualTo("application/x-seedarr-package"));
        Assert.That(httpContext.Response.Headers["Content-Disposition"].ToString(), Does.Contain("attachment; filename="));
        Assert.That(responseBody.Length, Is.GreaterThan(0));
    }

    private static byte[] CreateValidManifestBytes(string infoHash, string name = "Sample", string category = "Default")
    {
        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            ExportedAt = DateTime.UtcNow,
            Client = "Seedarr",
            Torrents = new List<PackageTorrentItem>
            {
                new PackageTorrentItem
                {
                    Id = 1,
                    Name = name,
                    InfoHash = infoHash,
                    Category = category,
                    TotalSize = 1024,
                    PieceCount = 1,
                    PieceLength = 1024,
                    Trackers = new List<PackageTrackerItem>
                    {
                        new PackageTrackerItem
                        {
                            Url = "http://tracker.example.com/announce",
                            Tier = 0,
                            Enabled = true,
                            Status = "Working"
                        }
                    }
                }
            }
        };

        return Encoding.UTF8.GetBytes(manifest.ToJson());
    }

    private static byte[] CreateGzipTarArchive(Action<TarWriter> writeEntries)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            writeEntries(tar);
        }

        return ms.ToArray();
    }

    private static void AddFileEntry(TarWriter writer, string entryName, byte[] content)
    {
        using var stream = new MemoryStream(content);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName)
        {
            DataStream = stream
        };
        writer.WriteEntry(entry);
    }

    private static void AddSymlinkEntry(TarWriter writer, string entryName, string linkTarget)
    {
        var entry = new PaxTarEntry(TarEntryType.SymbolicLink, entryName)
        {
            LinkName = linkTarget
        };
        writer.WriteEntry(entry);
    }

    private static void AddHardlinkEntry(TarWriter writer, string entryName, string linkTarget)
    {
        var entry = new PaxTarEntry(TarEntryType.HardLink, entryName)
        {
            LinkName = linkTarget
        };
        writer.WriteEntry(entry);
    }
}
