using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Packages;
using NzbDrone.Core.Peers.Extensions;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Api.V1.Packages;

namespace NzbDrone.Core.Test.Packages;

[TestFixture]
public class PackageExportServiceTests
{
    private ITorrentService _torrentService;
    private ITorrentFileService _torrentFileService;
    private IFastResumeService _fastResumeService;
    private IFastResumeBencodeSerializer _bencodeSerializer;
    private ITagService _tagService;
    private IAppFolderInfo _appFolderInfo;
    private ISyntheticMetadataGenerator _syntheticMetadataGenerator;
    private PackageExportService _service;
    private string _tempDir;
    private Torrent _testTorrent;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_pkg_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _torrentService = Substitute.For<ITorrentService>();
        _torrentFileService = Substitute.For<ITorrentFileService>();
        _fastResumeService = Substitute.For<IFastResumeService>();
        _bencodeSerializer = new FastResumeBencodeSerializer();
        _tagService = Substitute.For<ITagService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _syntheticMetadataGenerator = new SyntheticMetadataGenerator();

        _appFolderInfo.AppDataFolder.Returns(_tempDir);

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

        _service = new PackageExportService(
            _torrentService,
            _torrentFileService,
            _fastResumeService,
            _bencodeSerializer,
            _tagService,
            _appFolderInfo,
            _syntheticMetadataGenerator);
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
    public async Task ExportPackageAsync_IncludePayloadFalse_CreatesValidArchiveWithoutContent()
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
        Assert.That(entries.ContainsKey("metainfo/4a5b6c7d8e9f0123456789abcdef0123456789ab.torrent"), Is.True, "metainfo should be present");
        Assert.That(entries.ContainsKey("fastresume/4a5b6c7d8e9f0123456789abcdef0123456789ab.fastresume"), Is.True, "fastresume should be present");

        // Verify manifest contents
        var manifestJson = Encoding.UTF8.GetString(entries["manifest.json"]);
        var manifest = manifestJson.FromJson<PackageManifest>();

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest.SchemaVersion, Is.EqualTo(1));
        Assert.That(manifest.Client, Is.EqualTo("Seedarr"));
        Assert.That(manifest.Torrents, Has.Count.EqualTo(1));

        var t = manifest.Torrents[0];
        Assert.That(t.Id, Is.EqualTo(1));
        Assert.That(t.Name, Is.EqualTo("Ubuntu 24.04 Desktop"));
        Assert.That(t.InfoHash, Is.EqualTo("4a5b6c7d8e9f0123456789abcdef0123456789ab"));
        Assert.That(t.Category, Is.EqualTo("Linux"));
        Assert.That(t.Tags, Does.Contain("linux"));
        Assert.That(t.Tags, Does.Contain("iso"));
        Assert.That(t.TotalSize, Is.EqualTo(131072));
        Assert.That(t.PieceCount, Is.EqualTo(2));
        Assert.That(t.PieceLength, Is.EqualTo(65536));
        Assert.That(t.IsPrivate, Is.False);

        // Verify fastresume is valid bencode
        Assert.That(_bencodeSerializer.IsBencode(entries["fastresume/4a5b6c7d8e9f0123456789abcdef0123456789ab.fastresume"]), Is.True);
        var resumeData = _bencodeSerializer.Deserialize(entries["fastresume/4a5b6c7d8e9f0123456789abcdef0123456789ab.fastresume"]);
        Assert.That(resumeData.InfoHash, Is.EqualTo("4a5b6c7d8e9f0123456789abcdef0123456789ab"));

        // Verify NO content entries exist
        Assert.That(entries.Keys.Any(k => k.StartsWith("content/")), Is.False);
    }

    [Test]
    public async Task ExportPackageAsync_IncludePayloadTrue_StreamsPayloadContentFromDisk()
    {
        var sampleBytes = Encoding.UTF8.GetBytes("Hello Seedarr payload test data!");
        var filePath = Path.Combine(_tempDir, "ubuntu.iso");
        await File.WriteAllBytesAsync(filePath, sampleBytes);

        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile>
        {
            new TorrentFile
            {
                TorrentId = 1,
                Path = "ubuntu.iso",
                Size = sampleBytes.Length,
                Wanted = true
            }
        });

        using var outputStream = new MemoryStream();
        await _service.ExportPackageAsync(outputStream, new[] { 1 }, includePayload: true);

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

        var expectedContentPath = "content/Ubuntu 24.04 Desktop/ubuntu.iso";
        Assert.That(entries.ContainsKey(expectedContentPath), Is.True, $"Archive should contain {expectedContentPath}");
        Assert.That(entries[expectedContentPath], Is.EqualTo(sampleBytes));
    }

    [Test]
    public void ExportPackageAsync_NonexistentTorrent_ThrowsArgumentException()
    {
        _torrentService.Get(999).Returns((Torrent)null);
        using var ms = new MemoryStream();

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _service.ExportPackageAsync(ms, new[] { 999 }, includePayload: false);
        });

        Assert.That(ex.Message, Does.Contain("None of the specified torrents were found"));
    }

    [Test]
    public void ExportPackageAsync_EmptyTorrentIds_ThrowsArgumentException()
    {
        using var ms = new MemoryStream();

        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await _service.ExportPackageAsync(ms, Array.Empty<int>(), includePayload: false);
        });
    }

    [Test]
    public async Task ExportPackageAsync_PartialMissingTorrents_ExportsExistingOnly()
    {
        _torrentService.Get(999).Returns((Torrent)null);
        using var ms = new MemoryStream();

        await _service.ExportPackageAsync(ms, new[] { 1, 999 }, includePayload: false);

        ms.Position = 0;
        await using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        var entryNames = new List<string>();
        while (await tarReader.GetNextEntryAsync() is { } entry)
        {
            entryNames.Add(entry.Name);
        }

        Assert.That(entryNames, Does.Contain("manifest.json"));
        Assert.That(entryNames, Does.Contain("metainfo/4a5b6c7d8e9f0123456789abcdef0123456789ab.torrent"));
        Assert.That(entryNames.Count(e => e.StartsWith("metainfo/")), Is.EqualTo(1));
    }

    [Test]
    public async Task ExportPackageAsync_MissingPayloadFileOnDisk_SkipsGracefully()
    {
        _torrentFileService.GetByTorrentId(1).Returns(new List<TorrentFile>
        {
            new TorrentFile
            {
                TorrentId = 1,
                Path = "nonexistent_file_on_disk.dat",
                Size = 1000,
                Wanted = true
            }
        });

        using var ms = new MemoryStream();
        await _service.ExportPackageAsync(ms, new[] { 1 }, includePayload: true);

        ms.Position = 0;
        await using var gzipStream = new GZipStream(ms, CompressionMode.Decompress);
        await using var tarReader = new TarReader(gzipStream);

        var entryNames = new List<string>();
        while (await tarReader.GetNextEntryAsync() is { } entry)
        {
            entryNames.Add(entry.Name);
        }

        Assert.That(entryNames, Does.Contain("manifest.json"));
        Assert.That(entryNames.Any(e => e.StartsWith("content/")), Is.False);
    }

    [Test]
    public void PackageController_ParseTorrentIds_ParsesCorrectly()
    {
        var result1 = PackageController.ParseTorrentIds("1, 2, 3", StringValues.Empty);
        Assert.That(result1, Is.EquivalentTo(new[] { 1, 2, 3 }));

        var result2 = PackageController.ParseTorrentIds(null, new StringValues(new[] { "4", "5,6" }));
        Assert.That(result2, Is.EquivalentTo(new[] { 4, 5, 6 }));

        var result3 = PackageController.ParseTorrentIds("invalid, -1, 0, 7", StringValues.Empty);
        Assert.That(result3, Is.EquivalentTo(new[] { 7 }));
    }

    [Test]
    public void PackageController_DeterminePackageFileName_SanitizesProperly()
    {
        var single = new[] { new Torrent { Name = "Ubuntu 24.04: LTS/Server?*\"" } };
        var fileName = PackageController.DeterminePackageFileName(single);
        Assert.That(fileName, Is.EqualTo("Ubuntu 24.04_ LTS_Server.seedarr"));

        var multiple = new[]
        {
            new Torrent { Name = "Torrent 1" },
            new Torrent { Name = "Torrent 2" }
        };
        var multiFileName = PackageController.DeterminePackageFileName(multiple);
        Assert.That(multiFileName, Is.EqualTo("package.seedarr"));
    }

    [Test]
    public async Task PackageController_Export_ReturnsBadRequestWhenNoIds()
    {
        var controller = new PackageController(_service, _torrentService);

        var result = await controller.Export(null, false);
        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task PackageController_Export_ReturnsNotFoundWhenTorrentDoesNotExist()
    {
        _torrentService.Get(999).Returns((Torrent)null);
        var controller = new PackageController(_service, _torrentService);

        var result = await controller.Export("999", false);
        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task PackageController_Export_StreamsAndSetsHeaders()
    {
        var controller = new PackageController(_service, _torrentService);
        var httpContext = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        httpContext.Response.Body = responseStream;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var result = await controller.Export("1", false);

        Assert.That(result, Is.InstanceOf<EmptyResult>());
        Assert.That(httpContext.Response.ContentType, Is.EqualTo("application/x-seedarr-package"));
        Assert.That(httpContext.Response.Headers.ContentDisposition.ToString(), Does.Contain("Ubuntu 24.04 Desktop.seedarr"));
        Assert.That(responseStream.Length, Is.GreaterThan(0));
    }
}
