using System.Collections.Generic;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class DiskSpaceCheckTest
{
    private IAppFolderInfo _appFolderInfo;
    private DiskSpaceCheck _subject;

    [SetUp]
    public void SetUp()
    {
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _subject = new DiskSpaceCheck(_appFolderInfo);
    }

    [Test]
    public void Check_should_return_ok_when_disk_has_plenty_of_free_space()
    {
        _appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_source_should_always_be_DiskSpace()
    {
        _appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());

        var result = _subject.Check();

        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
    }

    [Test]
    public void Check_should_return_ok_when_exception_occurs_accessing_app_folder()
    {
        _appFolderInfo.AppDataFolder.Returns((string)null);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
    }

    [Test]
    public void Check_should_not_throw_for_valid_path()
    {
        _appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());

        Assert.DoesNotThrow(() => _subject.Check());
    }

    [Test]
    public void Check_error_result_should_contain_DiskSpace_source()
    {
        _appFolderInfo.AppDataFolder.Returns(Path.GetTempPath());
        var result = _subject.Check();

        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
    }

    [Test]
    public void Check_should_emit_warning_when_secondary_download_volume_is_low_on_disk_space()
    {
        _appFolderInfo.AppDataFolder.Returns("/appdata");

        var configService = Substitute.For<IConfigService>();
        configService.DefaultSavePath.Returns("/downloads");

        var subject = new DiskSpaceCheck(
            _appFolderInfo,
            configService,
            getFreeSpaceOverride: root => root == "/downloads" ? 200 * 1024 * 1024 : 10L * 1024 * 1024 * 1024,
            getPathRootOverride: p => p.Contains("downloads") ? "/downloads" : "/");

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
        Assert.That(result.Message, Does.Contain("Low disk space on download volume (/downloads): 200 MB remaining"));
    }

    [Test]
    public void Check_should_emit_warning_when_secondary_watch_folder_volume_is_low_on_disk_space()
    {
        _appFolderInfo.AppDataFolder.Returns("/appdata");

        var configService = Substitute.For<IConfigService>();
        configService.WatchFolderPath.Returns("/watch");

        var subject = new DiskSpaceCheck(
            _appFolderInfo,
            configService,
            getFreeSpaceOverride: root => root == "/watch" ? 150 * 1024 * 1024 : 10L * 1024 * 1024 * 1024,
            getPathRootOverride: p => p.Contains("watch") ? "/watch" : "/");

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
        Assert.That(result.Message, Does.Contain("Low disk space on watch folder volume (/watch): 150 MB remaining"));
    }

    [Test]
    public void Check_should_emit_error_when_app_data_volume_is_low_on_disk_space()
    {
        _appFolderInfo.AppDataFolder.Returns("/appdata");

        var subject = new DiskSpaceCheck(
            _appFolderInfo,
            null,
            getFreeSpaceOverride: root => 100 * 1024 * 1024,
            getPathRootOverride: p => "/");

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
        Assert.That(result.Message, Does.Contain("Low disk space: 100 MB remaining on /"));
    }

    [Test]
    public void Check_should_group_paths_on_same_volume_root()
    {
        _appFolderInfo.AppDataFolder.Returns("/data/app");

        var configService = Substitute.For<IConfigService>();
        configService.DefaultSavePath.Returns("/data/downloads");

        var checkCount = 0;
        var subject = new DiskSpaceCheck(
            _appFolderInfo,
            configService,
            getFreeSpaceOverride: root =>
            {
                checkCount++;
                return 5L * 1024 * 1024 * 1024;
            },
            getPathRootOverride: p => "/data");

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(checkCount, Is.EqualTo(1));
    }

    [Test]
    public void Check_should_resolve_linux_mount_point_for_app_data_via_disk_space_service_when_healthy()
    {
        _appFolderInfo.AppDataFolder.Returns("/home/user/.config/seedarr");

        var diskSpaceService = Substitute.For<IDiskSpaceService>();
        diskSpaceService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new() { Path = "/", Label = "Root Drive", FreeSpace = 100L * 1024 * 1024, TotalSpace = 50L * 1024 * 1024 * 1024 },
            new() { Path = "/home", Label = "AppData", FreeSpace = 200L * 1024 * 1024 * 1024, TotalSpace = 500L * 1024 * 1024 * 1024 },
        });

        var subject = new DiskSpaceCheck(_appFolderInfo, diskSpaceService: diskSpaceService);

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
        Assert.That(result.Message, Does.Contain("Low disk space on volume (/): 100 MB remaining"));
    }

    [Test]
    public void Check_should_emit_error_when_app_data_linux_mount_point_is_low_on_space_via_disk_space_service()
    {
        _appFolderInfo.AppDataFolder.Returns("/home/user/.config/seedarr");

        var diskSpaceService = Substitute.For<IDiskSpaceService>();
        diskSpaceService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new() { Path = "/", Label = "Root Drive", FreeSpace = 50L * 1024 * 1024 * 1024, TotalSpace = 50L * 1024 * 1024 * 1024 },
            new() { Path = "/home", Label = "AppData", FreeSpace = 200L * 1024 * 1024, TotalSpace = 500L * 1024 * 1024 * 1024 },
        });

        var subject = new DiskSpaceCheck(_appFolderInfo, diskSpaceService: diskSpaceService);

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
        Assert.That(result.Message, Does.Contain("Low disk space: 200 MB remaining on /home"));
    }

    [Test]
    public void Check_should_emit_warning_when_category_or_download_volume_is_low_via_disk_space_service()
    {
        _appFolderInfo.AppDataFolder.Returns("/var/lib/seedarr");

        var diskSpaceService = Substitute.For<IDiskSpaceService>();
        diskSpaceService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new() { Path = "/", Label = "Root Drive", FreeSpace = 20L * 1024 * 1024 * 1024, TotalSpace = 50L * 1024 * 1024 * 1024 },
            new() { Path = "/var/lib/seedarr", Label = "AppData", FreeSpace = 20L * 1024 * 1024 * 1024, TotalSpace = 50L * 1024 * 1024 * 1024 },
            new() { Path = "/mnt/storage", Label = "Movies", FreeSpace = 300L * 1024 * 1024, TotalSpace = 2000L * 1024 * 1024 * 1024 },
        });

        var subject = new DiskSpaceCheck(_appFolderInfo, diskSpaceService: diskSpaceService);

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
        Assert.That(result.Message, Does.Contain("Low disk space on download volume (/mnt/storage): 300 MB remaining"));
    }

    [Test]
    public void Check_should_return_ok_when_all_volumes_from_disk_space_service_have_sufficient_space()
    {
        _appFolderInfo.AppDataFolder.Returns("/var/lib/seedarr");

        var diskSpaceService = Substitute.For<IDiskSpaceService>();
        diskSpaceService.GetDiskSpace().Returns(new List<DiskSpaceInfo>
        {
            new() { Path = "/", Label = "Root Drive", FreeSpace = 20L * 1024 * 1024 * 1024, TotalSpace = 50L * 1024 * 1024 * 1024 },
            new() { Path = "/var/lib/seedarr", Label = "AppData", FreeSpace = 10L * 1024 * 1024 * 1024, TotalSpace = 50L * 1024 * 1024 * 1024 },
            new() { Path = "/mnt/storage", Label = "Movies", FreeSpace = 500L * 1024 * 1024 * 1024, TotalSpace = 2000L * 1024 * 1024 * 1024 },
        });

        var subject = new DiskSpaceCheck(_appFolderInfo, diskSpaceService: diskSpaceService);

        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(result.Source, Is.EqualTo("DiskSpace"));
    }
}
