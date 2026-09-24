using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Packages;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Torrents.Package;
using Seedarr.Api.V1.Packages;

namespace NzbDrone.Core.Test.Packages;

[TestFixture]
public class PackageManagementTests
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private ITorrentImportService _torrentImportService;
    private IFastResumeService _fastResumeService;
    private IFastResumeBencodeSerializer _bencodeSerializer;
    private ITagService _tagService;
    private IAppFolderInfo _appFolderInfo;
    private ISyntheticMetadataGenerator _syntheticMetadataGenerator;
    private ITrackerEntryService _trackerEntryService;
    private IDiskProvider _diskProvider;

    private PackageExportService _exportService;
    private PackageImportService _importService;
    private TorrentPackageService _torrentPackageService;
    private string _tempDir;
    private string _appDataDir;
    private string _sandboxDir;
    private Torrent _testTorrent;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_pkg_mgmt_" + Guid.NewGuid().ToString("N"));
        _appDataDir = Path.Combine(_tempDir, "appdata");
        _sandboxDir = Path.Combine(_tempDir, "sandbox");
        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(_sandboxDir);

        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _torrentImportService = Substitute.For<ITorrentImportService>();
        _fastResumeService = Substitute.For<IFastResumeService>();
        _bencodeSerializer = new FastResumeBencodeSerializer();
        _tagService = Substitute.For<ITagService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _syntheticMetadataGenerator = new SyntheticMetadataGenerator();
        _trackerEntryService = Substitute.For<ITrackerEntryService>();
        _diskProvider = Substitute.For<IDiskProvider>();

        _appFolderInfo.AppDataFolder.Returns(_appDataDir);
        _diskProvider.GetAvailableFreeSpace(Arg.Any<string>()).Returns(100L * 1024 * 1024 * 1024);

        _testTorrent = new Torrent
        {
            Id = 1,
            Name = "Arch Linux 2026.09",
            InfoHash = "11223344556677889900aabbccddeeff00112233",
            Category = "Operating Systems",
            TagIds = new List<int> { 1, 2 },
            TotalSize = 1048576,
            PieceCount = 4,
            PieceLength = 262144,
            IsPrivate = false,
            SavePath = _tempDir,
            Status = TorrentStatus.Seeding,
            Progress = 1.0,
            Uploaded = 2097152,
            Downloaded = 1048576,
            TrackerUrl = "http://tracker.example.com/announce"
        };

        _torrentService.Get(1).Returns(_testTorrent);
        _torrentService.GetByInfoHash("11223344556677889900aabbccddeeff00112233").Returns(_testTorrent);
        _tagService.GetLabelsForTagIds(Arg.Any<IEnumerable<int>>()).Returns(new List<string> { "linux", "distro" });

        _exportService = new PackageExportService(
            _torrentService,
            _torrentFileService,
            _fastResumeService,
            _bencodeSerializer,
            _tagService,
            _appFolderInfo,
            _syntheticMetadataGenerator,
            _trackerEntryService);

        _importService = new PackageImportService(
            _torrentService,
            _torrentImportService,
            _fastResumeService,
            _appFolderInfo,
            _diskProvider,
            _trackerEntryService,
            _bencodeSerializer);

        _torrentPackageService = new TorrentPackageService(
            _torrentService,
            _exportService,
            _importService);
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
    public async Task ExportPackageAsync_CreatesValidTarGzArchive_WithHeaderAndAllManifestMetadata()
    {
        _trackerEntryService.GetByTorrentId(1).Returns(new List<TrackerEntry>
        {
            new TrackerEntry
            {
                TorrentId = 1,
                Url = "http://tracker.example.com/announce",
                Tier = 0,
                Status = TrackerStatus.Working,
                Enabled = true,
                Seeders = 50,
                Leechers = 10,
                TotalAnnounces = 12,
                SuccessfulAnnounces = 12
            }
        });

        using var outputStream = new MemoryStream();
        await _exportService.ExportPackageAsync(outputStream, new[] { 1 }, includePayload: false);

        var archiveBytes = outputStream.ToArray();
        Assert.That(archiveBytes.Length, Is.GreaterThan(2));
        Assert.That(archiveBytes[0], Is.EqualTo(0x1F), "First byte must match GZip magic header 0x1F");
        Assert.That(archiveBytes[1], Is.EqualTo(0x8B), "Second byte must match GZip magic header 0x8B");

        using var decompressStream = new MemoryStream(archiveBytes);
        await using var gzipStream = new GZipStream(decompressStream, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        var entries = new Dictionary<string, byte[]>();
        TarEntry entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
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

        Assert.That(entries.ContainsKey("manifest.json"), Is.True);
        Assert.That(entries.ContainsKey("metainfo/11223344556677889900aabbccddeeff00112233.torrent"), Is.True);
        Assert.That(entries.ContainsKey("fastresume/11223344556677889900aabbccddeeff00112233.fastresume"), Is.True);

        var manifestJson = Encoding.UTF8.GetString(entries["manifest.json"]);
        var manifest = manifestJson.FromJson<PackageManifest>();

        Assert.That(manifest.SchemaVersion, Is.EqualTo(1));
        Assert.That(manifest.Client, Is.EqualTo("Seedarr"));
        Assert.That(manifest.Torrents.Count, Is.EqualTo(1));

        var exportedTorrent = manifest.Torrents[0];
        Assert.That(exportedTorrent.Id, Is.EqualTo(1));
        Assert.That(exportedTorrent.Name, Is.EqualTo("Arch Linux 2026.09"));
        Assert.That(exportedTorrent.InfoHash, Is.EqualTo("11223344556677889900aabbccddeeff00112233"));
        Assert.That(exportedTorrent.Category, Is.EqualTo("Operating Systems"));
        Assert.That(exportedTorrent.Tags, Is.EquivalentTo(new[] { "linux", "distro" }));
        Assert.That(exportedTorrent.Trackers.Count, Is.EqualTo(1));
        Assert.That(exportedTorrent.Trackers[0].Url, Is.EqualTo("http://tracker.example.com/announce"));
        Assert.That(exportedTorrent.Trackers[0].Seeders, Is.EqualTo(50));
        Assert.That(exportedTorrent.Trackers[0].Leechers, Is.EqualTo(10));
    }

    [Test]
    public async Task ExportPackageAsync_WithSourcePathMetainfo_UsesFileFromDisk()
    {
        var customTorrentPath = Path.Combine(_tempDir, "custom_source.torrent");
        var customBytes = Encoding.UTF8.GetBytes("d8:announce18:http://custom.local4:infod4:name4:teste");
        await File.WriteAllBytesAsync(customTorrentPath, customBytes);

        _testTorrent.SourcePath = customTorrentPath;

        using var ms = new MemoryStream();
        await _exportService.ExportPackageAsync(ms, new[] { 1 }, includePayload: false);

        ms.Position = 0;
        await using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        byte[] extractedMetainfo = null;
        TarEntry entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            if (entry.Name == "metainfo/11223344556677889900aabbccddeeff00112233.torrent")
            {
                using var readMs = new MemoryStream();
                await entry.DataStream.CopyToAsync(readMs);
                extractedMetainfo = readMs.ToArray();
            }
        }

        Assert.That(extractedMetainfo, Is.Not.Null);
        Assert.That(extractedMetainfo, Is.EqualTo(customBytes));
    }

    [Test]
    public async Task ExportPackageAsync_WithAppDataMetainfo_UsesAppDataFallbackPath()
    {
        var torrentsDir = Path.Combine(_appDataDir, "torrents");
        Directory.CreateDirectory(torrentsDir);
        var appDataTorrentPath = Path.Combine(torrentsDir, "11223344556677889900aabbccddeeff00112233.torrent");
        var appDataBytes = Encoding.UTF8.GetBytes("d8:announce19:http://appdata.local4:infod4:name4:teste");
        await File.WriteAllBytesAsync(appDataTorrentPath, appDataBytes);

        using var ms = new MemoryStream();
        await _exportService.ExportPackageAsync(ms, new[] { 1 }, includePayload: false);

        ms.Position = 0;
        await using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        byte[] extractedMetainfo = null;
        TarEntry entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            if (entry.Name == "metainfo/11223344556677889900aabbccddeeff00112233.torrent")
            {
                using var readMs = new MemoryStream();
                await entry.DataStream.CopyToAsync(readMs);
                extractedMetainfo = readMs.ToArray();
            }
        }

        Assert.That(extractedMetainfo, Is.Not.Null);
        Assert.That(extractedMetainfo, Is.EqualTo(appDataBytes));
    }

    [Test]
    public async Task ExportPackageAsync_WithSavedFastResumeFile_EmbedsExistingResumeData()
    {
        var resumeDir = Path.Combine(_appDataDir, "fastresume");
        Directory.CreateDirectory(resumeDir);
        var resumePath = Path.Combine(resumeDir, "11223344556677889900aabbccddeeff00112233.fastresume");

        var resumeData = new FastResumeData
        {
            InfoHash = "11223344556677889900aabbccddeeff00112233",
            SavePath = "/media/downloads",
            Progress = 1.0,
            Status = "Seeding",
            Uploaded = 9999
        };
        var resumeBytes = _bencodeSerializer.Serialize(resumeData);
        await File.WriteAllBytesAsync(resumePath, resumeBytes);

        using var ms = new MemoryStream();
        await _exportService.ExportPackageAsync(ms, new[] { 1 }, includePayload: false);

        ms.Position = 0;
        await using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        FastResumeData deserialized = null;
        TarEntry entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            if (entry.Name == "fastresume/11223344556677889900aabbccddeeff00112233.fastresume")
            {
                using var readMs = new MemoryStream();
                await entry.DataStream.CopyToAsync(readMs);
                deserialized = _bencodeSerializer.Deserialize(readMs.ToArray());
            }
        }

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized.SavePath, Is.EqualTo("/media/downloads"));
        Assert.That(deserialized.Uploaded, Is.EqualTo(9999));
    }

    [Test]
    public async Task ExportPackageAsync_MultiTorrent_CreatesSinglePackageWithAllTorrents()
    {
        var torrent2 = new Torrent
        {
            Id = 2,
            Name = "Debian 13 Bookworm",
            InfoHash = "99887766554433221100ffeeddccbbaa00998877",
            Category = "Linux",
            TotalSize = 2048,
            PieceCount = 1,
            PieceLength = 2048,
            SavePath = _tempDir,
            Status = TorrentStatus.Seeding,
            Progress = 1.0
        };

        _torrentService.Get(2).Returns(torrent2);

        using var ms = new MemoryStream();
        await _exportService.ExportPackageAsync(ms, new[] { 1, 2 }, includePayload: false);

        ms.Position = 0;
        await using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        var entryNames = new List<string>();
        TarEntry entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            entryNames.Add(entry.Name);
        }

        Assert.That(entryNames, Does.Contain("manifest.json"));
        Assert.That(entryNames, Does.Contain("metainfo/11223344556677889900aabbccddeeff00112233.torrent"));
        Assert.That(entryNames, Does.Contain("metainfo/99887766554433221100ffeeddccbbaa00998877.torrent"));
        Assert.That(entryNames, Does.Contain("fastresume/11223344556677889900aabbccddeeff00112233.fastresume"));
        Assert.That(entryNames, Does.Contain("fastresume/99887766554433221100ffeeddccbbaa00998877.fastresume"));
    }

    [Test]
    public async Task ExportPackageAsync_PayloadAsDirectory_StreamsAllContainedFilesIntoContentFolder()
    {
        var payloadDir = Path.Combine(_tempDir, "Arch Linux 2026.09");
        Directory.CreateDirectory(payloadDir);
        var subDir = Path.Combine(payloadDir, "subfolder");
        Directory.CreateDirectory(subDir);

        var file1 = Path.Combine(payloadDir, "archlinux.iso");
        var file2 = Path.Combine(subDir, "checksums.txt");
        await File.WriteAllBytesAsync(file1, Encoding.UTF8.GetBytes("iso-bytes-data"));
        await File.WriteAllBytesAsync(file2, Encoding.UTF8.GetBytes("sha256-checksums"));

        _testTorrent.SavePath = _tempDir;

        using var ms = new MemoryStream();
        await _exportService.ExportPackageAsync(ms, new[] { 1 }, includePayload: true);

        ms.Position = 0;
        await using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        var entryNames = new List<string>();
        TarEntry entry;
        while ((entry = await tarReader.GetNextEntryAsync()) != null)
        {
            entryNames.Add(entry.Name);
        }

        Assert.That(entryNames, Does.Contain("content/Arch Linux 2026.09/archlinux.iso"));
        Assert.That(entryNames, Does.Contain("content/Arch Linux 2026.09/subfolder/checksums.txt"));
    }

    [Test]
    public void ExportPackageAsync_CancellationToken_AbortsExportPromptly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var ms = new MemoryStream();
        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            await _exportService.ExportPackageAsync(ms, new[] { 1 }, includePayload: false, cancellationToken: cts.Token);
        });
    }

    [Test]
    public void ExportPackageAsync_NullArguments_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _exportService.ExportPackageAsync(null, new[] { 1 }, false);
        });

        using var ms = new MemoryStream();
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _exportService.ExportPackageAsync(ms, null, false);
        });
    }

    [Test]
    public async Task ImportPackageAsync_UncompressedTarArchive_DetectsAndImportsSuccessfully()
    {
        const string hash = "aabbccddee11223344556677889900aabbccddee";
        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            Client = "Seedarr",
            ExportedAt = DateTime.UtcNow,
            Torrents = new List<PackageTorrentItem>
            {
                new PackageTorrentItem
                {
                    Id = 5,
                    Name = "Uncompressed Test",
                    InfoHash = hash,
                    TotalSize = 512
                }
            }
        };

        var manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());
        var torrentBytes = Encoding.UTF8.GetBytes("d8:announce10:http://t.co4:infod4:name4:teste");

        using var tarMs = new MemoryStream();
        using (var tarWriter = new TarWriter(tarMs, TarEntryFormat.Pax, leaveOpen: true))
        {
            var mEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            tarWriter.WriteEntry(mEntry);

            var tEntry = new PaxTarEntry(TarEntryType.RegularFile, $"metainfo/{hash}.torrent")
            {
                DataStream = new MemoryStream(torrentBytes)
            };
            tarWriter.WriteEntry(tEntry);
        }

        tarMs.Position = 0;

        var options = new PackageImportOptions
        {
            TargetRootDir = _sandboxDir,
            RestoreTorrents = false
        };

        var result = await _importService.ImportPackageAsync(tarMs, options);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Torrents.Count, Is.EqualTo(1));
        Assert.That(result.Torrents[0].InfoHash, Is.EqualTo(hash));
    }

    [Test]
    public async Task ImportPackageAsync_ChecksumVerification_ValidatesInfoHashAndMatchesPayload()
    {
        const string hash = "33445566778899001122aabbccddeeff00112233";
        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            Client = "Seedarr",
            ExportedAt = DateTime.UtcNow,
            Torrents = new List<PackageTorrentItem>
            {
                new PackageTorrentItem
                {
                    Id = 10,
                    Name = "Verified Torrent",
                    InfoHash = hash,
                    Category = "Media",
                    TotalSize = 2048,
                    Tags = new List<string> { "tag1" }
                }
            }
        };

        var manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJson());
        var metainfoBytes = Encoding.UTF8.GetBytes("d8:announce11:http://tk.org4:infod4:name4:teste");

        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(manifestBytes)
            });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"metainfo/{hash}.torrent")
            {
                DataStream = new MemoryStream(metainfoBytes)
            });
        }

        ms.Position = 0;

        var createdTorrent = new Torrent
        {
            Id = 101,
            Name = "Verified Torrent",
            InfoHash = hash,
            Category = "Media",
            TotalSize = 2048
        };
        _torrentImportService.ImportFromFile(Arg.Any<Stream>(), Arg.Any<string>()).Returns(createdTorrent);

        var options = new PackageImportOptions
        {
            TargetRootDir = _sandboxDir,
            RestoreTorrents = true
        };

        var result = await _importService.ImportPackageAsync(ms, options);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Torrents[0].InfoHash, Is.EqualTo(hash));
        Assert.That(result.Torrents[0].TotalSize, Is.EqualTo(2048));
    }

    [Test]
    public void ImportPackageAsync_CompressionRatioExceeded_ThrowsSecurityException()
    {
        // 120 KB of zeroes that compress into < 200 bytes (over 50:1 ratio)
        var highlyCompressible = new byte[120 * 1024];

        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            var manifest = new PackageManifest
            {
                SchemaVersion = 1,
                Client = "Seedarr",
                Torrents = new List<PackageTorrentItem>()
            };
            var mBytes = Encoding.UTF8.GetBytes(manifest.ToJson());

            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(mBytes)
            });

            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "content/sparse.bin")
            {
                DataStream = new MemoryStream(highlyCompressible)
            });
        }

        ms.Position = 0;

        var options = new PackageImportOptions
        {
            TargetRootDir = _sandboxDir,
            MaxCompressionRatio = 25.0,
            MinBytesForRatioCheck = 1024
        };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _importService.ImportPackageAsync(ms, options);
        });

        Assert.That(ex.Message, Does.Contain("compression ratio"));
    }

    [Test]
    public void ImportPackageAsync_DeclaredLengthExceedingMax_ThrowsSecurityException()
    {
        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            Client = "Seedarr",
            Torrents = new List<PackageTorrentItem>()
        };
        var mBytes = Encoding.UTF8.GetBytes(manifest.ToJson());

        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(mBytes)
            });

            var bigEntry = new PaxTarEntry(TarEntryType.RegularFile, "huge_file.dat")
            {
                DataStream = new MemoryStream(new byte[100])
            };
            tar.WriteEntry(bigEntry);
        }

        ms.Position = 0;

        var options = new PackageImportOptions
        {
            TargetRootDir = _sandboxDir,
            MaxUncompressedBytes = 50
        };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _importService.ImportPackageAsync(ms, options);
        });

        Assert.That(ex.Message, Does.Contain("exceeds limit").Or.Contain("exceeded limit"));
    }

    [Test]
    public void ImportPackageAsync_MalformedManifestJson_ThrowsSecurityException()
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("{ this is not valid json :;; }"))
            });
        }

        ms.Position = 0;

        var options = new PackageImportOptions { TargetRootDir = _sandboxDir };

        var ex = Assert.ThrowsAsync<SecurityException>(async () =>
        {
            await _importService.ImportPackageAsync(ms, options);
        });

        Assert.That(ex.Message, Does.Contain("manifest JSON"));
    }

    [Test]
    public void ValidateTarEntryName_DetectsSecurityViolations()
    {
        Assert.Throws<SecurityException>(() => PackageImportService.ValidateTarEntryName(""));
        Assert.Throws<SecurityException>(() => PackageImportService.ValidateTarEntryName("   "));
        Assert.Throws<SecurityException>(() => PackageImportService.ValidateTarEntryName("C:\\Windows\\system32"));
        Assert.Throws<SecurityException>(() => PackageImportService.ValidateTarEntryName("d:/games/setup.exe"));
        Assert.Throws<SecurityException>(() => PackageImportService.ValidateTarEntryName("/etc/shadow"));
        Assert.Throws<SecurityException>(() => PackageImportService.ValidateTarEntryName("\\\\unc\\share"));
        Assert.Throws<SecurityException>(() => PackageImportService.ValidateTarEntryName("dir/../../secret.txt"));
        Assert.DoesNotThrow(() => PackageImportService.ValidateTarEntryName("content/valid/file.txt"));
    }

    [Test]
    public void VerifyFastResumePayload_EvaluatesIntegrityCorrectly()
    {
        var torrent = new Torrent
        {
            Name = "Linux Distro",
            SavePath = _tempDir,
            Status = TorrentStatus.Seeding,
            Progress = 1.0
        };

        var validFile = Path.Combine(_tempDir, "Linux Distro", "distro.iso");
        Directory.CreateDirectory(Path.GetDirectoryName(validFile)!);
        var bytes = Encoding.UTF8.GetBytes("sample iso image payload");
        File.WriteAllBytes(validFile, bytes);
        var fileInfo = new FileInfo(validFile);

        var validData = new FastResumeData
        {
            SavePath = _tempDir,
            Status = "Seeding",
            Progress = 1.0,
            Bitfield = new[] { true, true },
            Files = new List<FastResumeFileEntry>
            {
                new FastResumeFileEntry
                {
                    Path = "distro.iso",
                    Length = bytes.Length,
                    Mtime = fileInfo.LastWriteTimeUtc
                }
            }
        };

        var verified = PackageImportService.VerifyFastResumePayload(torrent, validData, out var reason);
        Assert.That(verified, Is.True, $"Verification should succeed: {reason}");

        // Incomplete bitfield and non-seeding status
        var incompleteData = new FastResumeData
        {
            SavePath = _tempDir,
            Status = "Downloading",
            Progress = 0.5,
            Bitfield = new[] { true, false }
        };
        var incompleteVerified = PackageImportService.VerifyFastResumePayload(torrent, incompleteData, out var incompleteReason);
        Assert.That(incompleteVerified, Is.False);
        Assert.That(incompleteReason, Does.Contain("incomplete"));

        // File size mismatch
        var badSizeData = new FastResumeData
        {
            SavePath = _tempDir,
            Status = "Seeding",
            Progress = 1.0,
            Bitfield = new[] { true },
            Files = new List<FastResumeFileEntry>
            {
                new FastResumeFileEntry
                {
                    Path = "distro.iso",
                    Length = bytes.Length + 100
                }
            }
        };
        var badSizeVerified = PackageImportService.VerifyFastResumePayload(torrent, badSizeData, out var badSizeReason);
        Assert.That(badSizeVerified, Is.False);
        Assert.That(badSizeReason, Does.Contain("size mismatch"));
    }

    [Test]
    public void PackagePathTranslator_TranslatesAndRemapsCrossPlatform()
    {
        var winPath = @"D:\Torrents\Movie 2026\movie.mkv";
        var translated = PackagePathTranslator.TranslatePath(
            winPath,
            sourcePrefix: @"D:\Torrents",
            destinationPrefix: "/data/media",
            destinationRoot: null,
            remappings: null,
            targetSeparator: '/');

        Assert.That(translated, Is.EqualTo("/data/media/Movie 2026/movie.mkv"));

        var remappings = new Dictionary<string, string>
        {
            { "C:/Old/Path", "/new/path" }
        };

        var remapped = PackagePathTranslator.TranslatePath("C:\\Old\\Path\\file.txt", remappings: remappings);
        Assert.That(remapped, Does.StartWith("/new/path"));
    }

    [Test]
    public void BoundedReadStream_CapabilitiesAndBoundsChecking()
    {
        var rawData = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        using var baseStream = new MemoryStream(rawData);
        using var bounded = new BoundedReadStream(baseStream, maxReadSize: 10);

        Assert.That(bounded.CanRead, Is.True);
        Assert.That(bounded.CanSeek, Is.True);
        Assert.That(bounded.CanWrite, Is.False);
        Assert.That(bounded.Length, Is.EqualTo(rawData.Length));

        var buffer = new byte[30];
        var read = bounded.Read(buffer, 0, 30);
        Assert.That(read, Is.EqualTo(10), "Should limit chunk size to maxReadSize");

        var span = new byte[25].AsSpan();
        var spanRead = bounded.Read(span);
        Assert.That(spanRead, Is.EqualTo(10));

        bounded.Position = 0;
        Assert.That(bounded.Position, Is.EqualTo(0));
        bounded.Seek(4, SeekOrigin.Begin);
        Assert.That(bounded.Position, Is.EqualTo(4));

        Assert.Throws<NotSupportedException>(() => bounded.Write(buffer, 0, 1));
        Assert.Throws<NotSupportedException>(() => bounded.SetLength(100));
    }

    [Test]
    public async Task CountingStream_TracksReadBytes()
    {
        var raw = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        using var mem = new MemoryStream(raw);
        using var counting = new CountingStream(mem);

        var buf = new byte[4];
        var read1 = counting.Read(buf, 0, 4);
        Assert.That(read1, Is.EqualTo(4));
        Assert.That(counting.BytesRead, Is.EqualTo(4));

        var read2 = await counting.ReadAsync(buf.AsMemory(0, 3));
        Assert.That(read2, Is.EqualTo(3));
        Assert.That(counting.BytesRead, Is.EqualTo(7));
    }

    [Test]
    public async Task PrefixStream_PrependsPrefixCorrectly()
    {
        var prefix = new byte[] { 0xAA, 0xBB };
        var innerData = new byte[] { 0xCC, 0xDD, 0xEE };
        using var innerMem = new MemoryStream(innerData);
        using var prefixStream = new PrefixStream(prefix, innerMem);

        Assert.That(prefixStream.CanRead, Is.True);
        Assert.That(prefixStream.CanSeek, Is.False);
        Assert.That(prefixStream.CanWrite, Is.False);
        Assert.That(prefixStream.Length, Is.EqualTo(5));

        var fullBuffer = new byte[5];
        var totalRead = await prefixStream.ReadAsync(fullBuffer.AsMemory(0, 5));

        Assert.That(totalRead, Is.EqualTo(5));
        Assert.That(fullBuffer, Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE }));
    }

    [Test]
    public async Task Controller_Export_Get_ReturnsBadRequestWhenEmptyIds()
    {
        var controller = new PackageController(_exportService, _torrentService, _importService);

        var result = await controller.Export(torrentIds: "", includePayload: false);
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Controller_Export_Get_ReturnsNotFoundWhenTorrentMissing()
    {
        _torrentService.Get(404).Returns((Torrent)null);
        var controller = new PackageController(_exportService, _torrentService, _importService);

        var result = await controller.Export(torrentIds: "404", includePayload: false);
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Controller_Export_Get_SetsContentTypeAndFilenameHeader()
    {
        var controller = new PackageController(_exportService, _torrentService, _importService);
        var httpContext = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        httpContext.Response.Body = responseStream;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Export(torrentIds: "1", includePayload: false);

        Assert.That(result, Is.InstanceOf<EmptyResult>());
        Assert.That(httpContext.Response.ContentType, Is.EqualTo("application/x-seedarr-package"));
        Assert.That(httpContext.Response.Headers.ContentDisposition.ToString(), Does.Contain("Arch Linux 2026.09.seedarr"));
        Assert.That(responseStream.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task Controller_Export_WhenServicesNull_Returns501NotImplemented()
    {
        var controller = new PackageController(
            (IPackageExportService)null,
            _torrentService,
            null,
            null);

        var result = await controller.Export(torrentIds: "1", includePayload: false);
        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var objResult = (ObjectResult)result;
        Assert.That(objResult.StatusCode, Is.EqualTo(StatusCodes.Status501NotImplemented));
    }

    [Test]
    public async Task Controller_ExportPost_WithRequestBody_ExportsTorrentsFromList()
    {
        var controller = new PackageController(_exportService, _torrentService, _importService);
        var httpContext = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        httpContext.Response.Body = responseStream;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new PackageExportRequest
        {
            TorrentIds = new List<int> { 1 },
            IncludePayload = false
        };

        var result = await controller.ExportPost(request: request);

        Assert.That(result, Is.InstanceOf<EmptyResult>());
        Assert.That(responseStream.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task Controller_ExportPost_WithEmptyBody_FallsBackToQueryParam()
    {
        var controller = new PackageController(_exportService, _torrentService, _importService);
        var httpContext = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        httpContext.Response.Body = responseStream;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.ExportPost(request: null, torrentIds: "1", includePayload: false);

        Assert.That(result, Is.InstanceOf<EmptyResult>());
        Assert.That(responseStream.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task Controller_ExportPost_WithNoIds_ReturnsBadRequest()
    {
        var controller = new PackageController(_exportService, _torrentService, _importService);
        var request = new PackageExportRequest { TorrentIds = new List<int>() };

        var result = await controller.ExportPost(request: request);
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Controller_Import_WithValidFormFile_ReturnsOkResult()
    {
        const string hash = "77889900112233445566aabbccddeeff00112233";
        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            Client = "Seedarr",
            Torrents = new List<PackageTorrentItem>
            {
                new PackageTorrentItem { Id = 1, InfoHash = hash, Name = "Imported Torrent" }
            }
        };

        using var archiveMs = new MemoryStream();
        using (var gzip = new GZipStream(archiveMs, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(manifest.ToJson()))
            });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"metainfo/{hash}.torrent")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("torrent-bytes"))
            });
        }

        var archiveBytes = archiveMs.ToArray();
        using var fileStream = new MemoryStream(archiveBytes);
        var formFile = new FormFile(fileStream, 0, archiveBytes.Length, "file", "import.seedarr");

        var controller = new PackageController(_exportService, _torrentService, _importService);
        var response = await controller.Import(file: formFile);

        Assert.That(response, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response;
        Assert.That(okResult.Value, Is.InstanceOf<PackageImportResult>());
        var importResult = (PackageImportResult)okResult.Value;
        Assert.That(importResult.Success, Is.True);
    }

    [Test]
    public async Task Controller_Import_WithRequestBodyStream_ReadsAndImports()
    {
        const string hash = "88990011223344556677aabbccddeeff00112233";
        var manifest = new PackageManifest
        {
            SchemaVersion = 1,
            Client = "Seedarr",
            Torrents = new List<PackageTorrentItem>
            {
                new PackageTorrentItem { Id = 2, InfoHash = hash, Name = "Body Stream Torrent" }
            }
        };

        using var archiveMs = new MemoryStream();
        using (var gzip = new GZipStream(archiveMs, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(manifest.ToJson()))
            });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"metainfo/{hash}.torrent")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("torrent-bytes"))
            });
        }

        var archiveBytes = archiveMs.ToArray();
        var controller = new PackageController(_exportService, _torrentService, _importService);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(archiveBytes);
        httpContext.Request.ContentLength = archiveBytes.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var response = await controller.Import(file: null);

        Assert.That(response, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)response;
        var importResult = (PackageImportResult)okResult.Value;
        Assert.That(importResult.Success, Is.True);
    }

    [Test]
    public async Task Controller_Import_NoFileOrBody_ReturnsBadRequest()
    {
        var controller = new PackageController(_exportService, _torrentService, _importService);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var response = await controller.Import(file: null);
        Assert.That(response, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Controller_Import_WhenServicesNull_Returns501NotImplemented()
    {
        var controller = new PackageController(
            _exportService,
            _torrentService,
            (IPackageImportService)null,
            null);

        var response = await controller.Import(file: null);
        Assert.That(response, Is.InstanceOf<ObjectResult>());
        var objResult = (ObjectResult)response;
        Assert.That(objResult.StatusCode, Is.EqualTo(StatusCodes.Status501NotImplemented));
    }

    [Test]
    public async Task Controller_Import_OnSecurityException_ReturnsBadRequest()
    {
        var mockImportService = Substitute.For<IPackageImportService>();
        mockImportService.ImportPackageAsync(Arg.Any<Stream>(), Arg.Any<PackageImportOptions>(), Arg.Any<CancellationToken>())
            .Returns<PackageImportResult>(_ => throw new SecurityException("Archive bomb detected"));

        var controller = new PackageController(_exportService, _torrentService, mockImportService);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var formFile = new FormFile(stream, 0, 3, "file", "bomb.seedarr");

        var response = await controller.Import(file: formFile);
        Assert.That(response, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)response;
        Assert.That(badRequest.Value.ToString(), Does.Contain("Archive bomb detected"));
    }

    [Test]
    public void Controller_ParseTorrentIds_HandlesMixedDelimitersAndDuplicates()
    {
        var parsed = PackageController.ParseTorrentIds("1, 2, 3, 2, 0, -5, abc", new StringValues(new[] { "3", "4", "5" }));
        Assert.That(parsed, Is.EquivalentTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void Controller_DeterminePackageFileName_HandlesEmptySingleAndMultiple()
    {
        var empty = PackageController.DeterminePackageFileName(null);
        Assert.That(empty, Is.EqualTo("package.seedarr"));

        var single = PackageController.DeterminePackageFileName(new[] { new Torrent { Name = "Super <Cool> :Movie" } });
        Assert.That(single, Is.EqualTo("Super _Cool_ _Movie.seedarr"));

        var multiple = PackageController.DeterminePackageFileName(new[]
        {
            new Torrent { Name = "T1" },
            new Torrent { Name = "T2" }
        });
        Assert.That(multiple, Is.EqualTo("package.seedarr"));
    }

    [Test]
    public void Controller_SanitizeFileName_StripsInvalidCharsCorrectly()
    {
        Assert.That(PackageController.SanitizeFileName(""), Is.EqualTo(""));
        Assert.That(PackageController.SanitizeFileName("   "), Is.EqualTo(""));
        Assert.That(PackageController.SanitizeFileName("normal_name.ext"), Is.EqualTo("normal_name.ext"));
        Assert.That(PackageController.SanitizeFileName("a*b?c/d\\e:f|g<h>i\"j;k"), Is.EqualTo("a_b_c_d_e_f_g_h_i_j_k"));
    }
}
