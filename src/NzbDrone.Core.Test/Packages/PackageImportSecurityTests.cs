using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
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
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Packages;

namespace NzbDrone.Core.Test.Packages;

[TestFixture]
public class PackageImportSecurityTests
{
    private ITorrentService _torrentService;
    private ITorrentImportService _torrentImportService;
    private IFastResumeService _fastResumeService;
    private IAppFolderInfo _appFolderInfo;
    private IDiskProvider _diskProvider;
    private PackageImportService _service;
    private string _tempDir;
    private string _sandboxDir;
    private string _appDataDir;
    private string _outsideDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_import_test_" + Guid.NewGuid().ToString("N"));
        _sandboxDir = Path.Combine(_tempDir, "sandbox");
        _appDataDir = Path.Combine(_tempDir, "appdata");
        _outsideDir = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(_sandboxDir);
        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(_outsideDir);

        _torrentService = Substitute.For<ITorrentService>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _fastResumeService = Substitute.For<IFastResumeService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _diskProvider = Substitute.For<IDiskProvider>();

        _appFolderInfo.AppDataFolder.Returns(_appDataDir);
        _diskProvider.GetAvailableFreeSpace(Arg.Any<string>()).Returns(100L * 1024 * 1024 * 1024);

        _service = new PackageImportService(
            _torrentService,
            _torrentImportService,
            _fastResumeService,
            _appFolderInfo,
            _diskProvider);
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
            // Best-effort test cleanup
        }
    }

    [Test]
    public void ZipSlip_RelativePathTraversal_ThrowsSecurityExceptionAndDoesNotWriteOutsideSandbox()
    {
        var outsideTarget = Path.Combine(_outsideDir, "malicious.txt");
        var traversalRelative = Path.GetRelativePath(_sandboxDir, outsideTarget);

        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddFileEntry(tar, traversalRelative, Encoding.UTF8.GetBytes("ATTACK PAYLOAD"));
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions { TargetRootDir = _sandboxDir };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("Zip-Slip"));
        Assert.That(File.Exists(outsideTarget), Is.False, "Malicious file must not be written outside the sandbox");
    }

    [Test]
    public void ZipSlip_ParentDirectoryEscape_ThrowsSecurityException()
    {
        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddFileEntry(tar, "../../malicious.txt", Encoding.UTF8.GetBytes("ATTACK"));
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
    public void SymlinkEntry_RejectedUnconditionallyWithSecurityException()
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
        Assert.That(File.Exists(Path.Combine(_sandboxDir, "symlink_evil")), Is.False);
    }

    [Test]
    public void HardlinkEntry_RejectedUnconditionallyWithSecurityException()
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
    public void DecompressionBomb_ExceedingByteCap_ThrowsSecurityException()
    {
        // 50 KB of uncompressed data
        var bigPayload = new byte[50 * 1024];
        Array.Fill<byte>(bigPayload, 0x41);

        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddFileEntry(tar, "bomb.dat", bigPayload);
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions
        {
            TargetRootDir = _sandboxDir,
            MaxUncompressedBytes = 10 * 1024 // Cap at 10 KB
        };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("exceeds limit").Or.Contain("total uncompressed size exceeded limit"));
    }

    [Test]
    public void DecompressionBomb_ExceedingCompressionRatio_ThrowsSecurityException()
    {
        // 200 KB of repetitive zeroes that compresses to ~200 bytes (1000:1 ratio)
        var zeroPayload = new byte[200 * 1024];

        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddFileEntry(tar, "sparse.dat", zeroPayload);
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions
        {
            TargetRootDir = _sandboxDir,
            MaxCompressionRatio = 20.0, // 20:1 max ratio
            MinBytesForRatioCheck = 1024 // Start checking after 1 KB
        };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("compression ratio"));
    }

    [Test]
    public async Task ValidPackage_CleanExtractionAndRestorationIntoSeedarr()
    {
        const string infoHash = "1234567890abcdef1234567890abcdef12345678";
        var manifestBytes = CreateValidManifestBytes(infoHash, "Test Torrent", "Movies");
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
            Name = "Test Torrent",
            InfoHash = infoHash,
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
        Assert.That(result.Torrents[0].Id, Is.EqualTo(42));
        Assert.That(result.Torrents[0].InfoHash, Is.EqualTo(infoHash));
        Assert.That(result.Torrents[0].Category, Is.EqualTo("Movies"));

        // Verify .torrent and .fastresume files restored into Seedarr dirs
        var restoredTorrent = Path.Combine(_appDataDir, "torrents", $"{infoHash}.torrent");
        var restoredFastResume = Path.Combine(_appDataDir, "fastresume", $"{infoHash}.fastresume");

        Assert.That(File.Exists(restoredTorrent), Is.True, "Restored torrent file must exist");
        Assert.That(File.Exists(restoredFastResume), Is.True, "Restored fastresume file must exist");
        Assert.That(await File.ReadAllBytesAsync(restoredTorrent), Is.EqualTo(torrentBytes));
        Assert.That(await File.ReadAllBytesAsync(restoredFastResume), Is.EqualTo(fastresumeBytes));

        _fastResumeService.Received(1).LoadFastResume(Arg.Is<Torrent>(t => t.Id == 42));
    }

    [Test]
    public void ManifestValidation_MissingManifest_ThrowsSecurityException()
    {
        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "metainfo/test.torrent", Encoding.UTF8.GetBytes("torrent"));
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions { TargetRootDir = _sandboxDir };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("missing required manifest.json"));
    }

    [Test]
    public void ManifestValidation_InvalidSchemaVersion_ThrowsSecurityException()
    {
        var manifest = new PackageManifest
        {
            SchemaVersion = 999, // Invalid / unsupported
            Client = "EvilClient"
        };
        var manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());

        var archiveBytes = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", manifestBytes);
        });

        using var archiveStream = new MemoryStream(archiveBytes);
        var options = new PackageImportOptions { TargetRootDir = _sandboxDir };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _service.ImportPackageAsync(archiveStream, options);
        });

        Assert.That(ex.Message, Does.Contain("Unsupported or invalid package manifest schema version"));
    }

    [Test]
    public async Task Controller_Import_ReturnsBadRequestOnSecurityException()
    {
        var controller = new PackageController(
            Substitute.For<IPackageExportService>(),
            _torrentService,
            _service);

        // Malicious archive with Zip-Slip
        var maliciousArchive = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes("abc"));
            AddFileEntry(tar, "../../malicious.txt", Encoding.UTF8.GetBytes("ATTACK"));
        });

        using var ms = new MemoryStream(maliciousArchive);
        var formFile = new FormFile(ms, 0, ms.Length, "file", "malicious.seedarr");

        var response = await controller.Import(formFile);

        Assert.That(response, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)response;
        Assert.That(badRequest.Value.ToString(), Does.Contain("Zip-Slip"));
    }

    [Test]
    public async Task Controller_Import_ReturnsOkOnValidArchive()
    {
        const string infoHash = "9999567890abcdef1234567890abcdef12345678";
        var validArchive = CreateGzipTarArchive(tar =>
        {
            AddFileEntry(tar, "manifest.json", CreateValidManifestBytes(infoHash, "Valid Torrent"));
            AddFileEntry(tar, $"metainfo/{infoHash}.torrent", Encoding.UTF8.GetBytes("torrent-bytes"));
        });

        var controller = new PackageController(
            Substitute.For<IPackageExportService>(),
            _torrentService,
            _service);

        using var ms = new MemoryStream(validArchive);
        var formFile = new FormFile(ms, 0, ms.Length, "file", "valid.seedarr");

        var response = await controller.Import(formFile);

        Assert.That(response, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response;
        Assert.That(okResult.Value, Is.InstanceOf<PackageImportResult>());
        var importResult = (PackageImportResult)okResult.Value;
        Assert.That(importResult.Success, Is.True);
    }

    [Test]
    public async Task Controller_Import_ReturnsBadRequestWhenNoFile()
    {
        var controller = new PackageController(
            Substitute.For<IPackageExportService>(),
            _torrentService,
            _service);

        var response = await controller.Import(null);

        Assert.That(response, Is.InstanceOf<BadRequestObjectResult>());
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
                    PieceLength = 1024
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
