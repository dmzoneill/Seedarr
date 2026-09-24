using System.IO;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;

namespace NzbDrone.Core.Test.HealthCheck.Checks;

[TestFixture]
public class AppFolderPermissionsCheckTest
{
    private IAppFolderInfo _appFolderInfo;

    [SetUp]
    public void SetUp()
    {
        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns("/appdata");
    }

    [Test]
    public void Check_should_return_error_when_appFolderInfo_is_null()
    {
        var subject = new AppFolderPermissionsCheck(null);
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Source, Is.EqualTo(nameof(AppFolderPermissionsCheck)));
        Assert.That(result.Message, Does.Contain("not configured"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Check_should_return_error_when_appDataFolder_is_blank(string folder)
    {
        _appFolderInfo.AppDataFolder.Returns(folder);
        var subject = new AppFolderPermissionsCheck(_appFolderInfo);
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
    }

    [Test]
    public void Check_should_return_ok_when_all_folders_are_writable()
    {
        var subject = new AppFolderPermissionsCheck(_appFolderInfo, _ => true);
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public void Check_should_return_error_when_appDataFolder_is_not_writable()
    {
        var subject = new AppFolderPermissionsCheck(_appFolderInfo, path => !path.Equals("/appdata"));
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("AppData folder '/appdata' is not writable"));
    }

    [Test]
    public void Check_should_return_warning_when_logs_folder_is_not_writable()
    {
        var subject = new AppFolderPermissionsCheck(_appFolderInfo, path => !path.Contains("logs"));
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("logs"));
    }

    [Test]
    public void Check_should_return_warning_when_backups_folder_is_not_writable()
    {
        var subject = new AppFolderPermissionsCheck(_appFolderInfo, path => !path.ToLowerInvariant().Contains("backup"));
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("backups"));
    }

    [Test]
    public void Check_should_return_warning_when_both_logs_and_backups_are_not_writable()
    {
        var subject = new AppFolderPermissionsCheck(_appFolderInfo, path => path.Equals("/appdata"));
        var result = subject.Check();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("logs"));
        Assert.That(result.Message, Does.Contain("backups"));
    }

    [Test]
    public void Check_should_execute_successfully_on_temp_directory_without_override()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_perm_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            _appFolderInfo.AppDataFolder.Returns(tempDir);
            var subject = new AppFolderPermissionsCheck(_appFolderInfo);
            var result = subject.Check();

            Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
            {
                // Best-effort test cleanup
            }
            }
        }
    }
}
