using System;
using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class HardlinkCapabilityCheckTest
{
    private IConfigService _configService;
    private IAppFolderInfo _appFolderInfo;
    private IHardlinkProvider _hardlinkProvider;
    private HardlinkCapabilityCheck _subject;
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _hardlinkProvider = Substitute.For<IHardlinkProvider>();

        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_hardlink_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _configService.TorrentSaveDirectory.Returns(_tempDir);
        _appFolderInfo.AppDataFolder.Returns(_tempDir);

        _subject = new HardlinkCapabilityCheck(_configService, _appFolderInfo, _hardlinkProvider);
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
                // Ignore cleanup error
            }
        }
    }

    [Test]
    public void Check_should_return_ok_when_hardlink_succeeds()
    {
        _hardlinkProvider.TryCreateHardLink(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<string>())
            .Returns(true);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(result.Source, Is.EqualTo("HardlinkCapability"));
    }

    [Test]
    public void Check_should_return_warning_when_hardlink_fails_across_devices()
    {
        _hardlinkProvider.TryCreateHardLink(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<string>())
            .Returns(x =>
            {
                x[2] = "EXDEV (Invalid cross-device link)";
                return false;
            });

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Source, Is.EqualTo("HardlinkCapability"));
        Assert.That(result.Message, Does.Contain("cross-device"));
    }

    [Test]
    public void Check_should_return_ok_when_directory_not_configured()
    {
        _configService.TorrentSaveDirectory.Returns(string.Empty);
        _configService.WatchFolderPath.Returns(string.Empty);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        Assert.That(result.Source, Is.EqualTo("HardlinkCapability"));
    }

    [Test]
    public void Check_should_return_notice_when_configured_directory_does_not_exist()
    {
        var nonExistent = Path.Combine(_tempDir, "does_not_exist_folder");
        _configService.TorrentSaveDirectory.Returns(nonExistent);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Notice));
        Assert.That(result.Source, Is.EqualTo("HardlinkCapability"));
    }

    [Test]
    public void Check_should_fallback_to_watch_folder_if_save_directory_empty()
    {
        _configService.TorrentSaveDirectory.Returns(string.Empty);
        _configService.WatchFolderPath.Returns(_tempDir);

        _hardlinkProvider.TryCreateHardLink(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<string>())
            .Returns(true);

        var result = _subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        _hardlinkProvider.Received(1).TryCreateHardLink(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<string>());
    }
}
