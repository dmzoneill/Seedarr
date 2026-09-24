using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaEnrichment;

namespace NzbDrone.Core.Test.MediaEnrichment;

[TestFixture]
public class MediaCoverEvictionTest
{
    private IConfigService _configService;
    private IAppFolderInfo _appFolderInfo;
    private MediaEnrichmentService _service;
    private string _tempDirectory;
    private string _mediaCoverDirectory;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "seedarr_eviction_test_" + Guid.NewGuid().ToString("N"));
        _mediaCoverDirectory = Path.Combine(_tempDirectory, "MediaCover");
        Directory.CreateDirectory(_mediaCoverDirectory);

        _configService = Substitute.For<IConfigService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempDirectory);

        _configService.MediaCoverMaxCacheSizeMb.Returns(1024);
        _configService.MediaCoverCacheTtlDays.Returns(60);

        _service = new MediaEnrichmentService(
            repository: null,
            inspector: null,
            configService: _configService,
            appFolderInfo: _appFolderInfo);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Best-effort test cleanup
            }
        }
    }

    [Test]
    public void EvictMediaCoverCache_WhenSizeExceedsQuota_ReducesUsageBelow85Percent()
    {
        // 1 MB quota = 1048576 bytes; 85% threshold = 891289 bytes
        _configService.MediaCoverMaxCacheSizeMb.Returns(1);
        _configService.MediaCoverCacheTtlDays.Returns(0); // disable TTL for this test

        var subDir = Path.Combine(_mediaCoverDirectory, "quota_test");
        Directory.CreateDirectory(subDir);

        // Create 5 files of 300,000 bytes each = 1,500,000 bytes total (> 1,048,576 bytes)
        for (var i = 0; i < 5; i++)
        {
            var filePath = Path.Combine(subDir, $"file_{i}.jpg");
            File.WriteAllBytes(filePath, new byte[300_000]);
            var accessTime = DateTime.UtcNow.AddHours(-10 + i);
            File.SetLastAccessTimeUtc(filePath, accessTime);
            File.SetLastWriteTimeUtc(filePath, accessTime);
        }

        _service.EvictMediaCoverCache();

        var remainingFiles = Directory.GetFiles(_mediaCoverDirectory, "*.*", SearchOption.AllDirectories);
        var remainingBytes = remainingFiles.Sum(f => new FileInfo(f).Length);
        var target85Percent = (long)(1024 * 1024 * 0.85);

        Assert.That(remainingBytes, Is.LessThan(target85Percent));
        Assert.That(remainingFiles.Length, Is.LessThan(5));
    }

    [Test]
    public void EvictMediaCoverCache_DeletesOldestAccessedFilesFirst_InLruOrder()
    {
        // 1 MB quota
        _configService.MediaCoverMaxCacheSizeMb.Returns(1);
        _configService.MediaCoverCacheTtlDays.Returns(0);

        var subDir = Path.Combine(_mediaCoverDirectory, "lru_test");
        Directory.CreateDirectory(subDir);

        var fileOld = Path.Combine(subDir, "old.jpg");
        var fileMedium = Path.Combine(subDir, "medium.jpg");
        var fileNew = Path.Combine(subDir, "new.jpg");

        // 3 files of 400 KB each = 1.2 MB total
        File.WriteAllBytes(fileOld, new byte[400_000]);
        File.WriteAllBytes(fileMedium, new byte[400_000]);
        File.WriteAllBytes(fileNew, new byte[400_000]);

        File.SetLastAccessTimeUtc(fileOld, DateTime.UtcNow.AddDays(-10));
        File.SetLastAccessTimeUtc(fileMedium, DateTime.UtcNow.AddDays(-5));
        File.SetLastAccessTimeUtc(fileNew, DateTime.UtcNow.AddMinutes(-5));

        _service.EvictMediaCoverCache();

        // Oldest file should be evicted first
        Assert.That(File.Exists(fileOld), Is.False);
        // Newest file should still be preserved
        Assert.That(File.Exists(fileNew), Is.True);
    }

    [Test]
    public void EvictMediaCoverCache_PrunesFilesOlderThanTtl()
    {
        _configService.MediaCoverCacheTtlDays.Returns(30);
        _configService.MediaCoverMaxCacheSizeMb.Returns(1024); // large quota

        var subDir = Path.Combine(_mediaCoverDirectory, "ttl_test");
        Directory.CreateDirectory(subDir);

        var expiredFile = Path.Combine(subDir, "expired.jpg");
        var activeFile = Path.Combine(subDir, "active.jpg");

        File.WriteAllBytes(expiredFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        File.WriteAllBytes(activeFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        File.SetLastAccessTimeUtc(expiredFile, DateTime.UtcNow.AddDays(-40));
        File.SetLastWriteTimeUtc(expiredFile, DateTime.UtcNow.AddDays(-40));

        File.SetLastAccessTimeUtc(activeFile, DateTime.UtcNow.AddDays(-5));
        File.SetLastWriteTimeUtc(activeFile, DateTime.UtcNow.AddDays(-5));

        _service.EvictMediaCoverCache();

        Assert.That(File.Exists(expiredFile), Is.False);
        Assert.That(File.Exists(activeFile), Is.True);
    }

    [Test]
    public void EvictMediaCoverCache_CleansUpEmptySubdirectories()
    {
        _configService.MediaCoverCacheTtlDays.Returns(30);

        var subDir = Path.Combine(_mediaCoverDirectory, "empty_dir_test");
        Directory.CreateDirectory(subDir);

        var expiredFile = Path.Combine(subDir, "poster.jpg");
        File.WriteAllBytes(expiredFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        File.SetLastAccessTimeUtc(expiredFile, DateTime.UtcNow.AddDays(-50));
        File.SetLastWriteTimeUtc(expiredFile, DateTime.UtcNow.AddDays(-50));

        _service.EvictMediaCoverCache();

        Assert.That(File.Exists(expiredFile), Is.False);
        Assert.That(Directory.Exists(subDir), Is.False);
        Assert.That(Directory.Exists(_mediaCoverDirectory), Is.True);
    }

    [Test]
    public async Task CacheArtworkAsync_WritesAtomically_AndNoTmpFilesRemain()
    {
        var sourceImage = Path.Combine(_tempDirectory, "source.jpg");
        var validJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        await File.WriteAllBytesAsync(sourceImage, validJpeg);

        var cachedPath = await _service.CacheArtworkAsync(sourceImage, 77, "poster");

        Assert.That(cachedPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(cachedPath), Is.True);

        var fileBytes = await File.ReadAllBytesAsync(cachedPath);
        Assert.That(fileBytes, Is.EqualTo(validJpeg));

        var tmpFiles = Directory.GetFiles(_mediaCoverDirectory, "*.tmp.*", SearchOption.AllDirectories);
        Assert.That(tmpFiles, Is.Empty);
    }

    [Test]
    public void MediaCoverMaintenanceTask_ExecutesEviction()
    {
        var enrichmentService = Substitute.For<IMediaEnrichmentService>();
        var task = new MediaCoverMaintenanceTask(enrichmentService);

        Assert.That(task.DefaultInterval, Is.EqualTo(1440));

        task.Execute();

        enrichmentService.Received(1).EvictMediaCoverCache();
    }
}
