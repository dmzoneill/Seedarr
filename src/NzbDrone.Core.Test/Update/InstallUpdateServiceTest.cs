using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.Update;

[TestFixture]
public class InstallUpdateServiceTest
{
    private string _tempDir;
    private string _targetDir;
    private IAppFolderInfo _appFolderInfo;
    private IPostUpdateVerificationService _postUpdateVerificationService;
    private IUpdatePackageProvider _updatePackageProvider;
    private IUpdateService _updateService;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SeedarrUpdateTest_" + Guid.NewGuid().ToString("N"));
        _targetDir = Path.Combine(_tempDir, "install");

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_targetDir);

        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempDir);
        _appFolderInfo.StartUpFolder.Returns(_targetDir);

        _postUpdateVerificationService = Substitute.For<IPostUpdateVerificationService>();
        _updatePackageProvider = Substitute.For<IUpdatePackageProvider>();
        _updateService = Substitute.For<IUpdateService>();
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
            // Ignore teardown errors
        }
    }

    [Test]
    public void Container_detection_should_report_true_when_override_set()
    {
        var service = new InstallUpdateService(
            _updateService,
            _updatePackageProvider,
            _postUpdateVerificationService,
            _appFolderInfo,
            installDirectory: _targetDir,
            isContainerizedOverride: true);

        Assert.That(service.IsContainerized, Is.True);
    }

    [Test]
    public void Container_detection_should_report_true_when_dotnet_running_in_container_env_set()
    {
        var prev = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
            var service = new InstallUpdateService(
                _updateService,
                _updatePackageProvider,
                _postUpdateVerificationService,
                _appFolderInfo,
                installDirectory: _targetDir);

            Assert.That(service.IsContainerized, Is.True);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", prev);
        }
    }

    [Test]
    public void InstallUpdateAsync_should_throw_and_set_failed_progress_when_containerized()
    {
        var service = new InstallUpdateService(
            _updateService,
            _updatePackageProvider,
            _postUpdateVerificationService,
            _appFolderInfo,
            installDirectory: _targetDir,
            isContainerizedOverride: true);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.InstallUpdateAsync("2.0.0");
        });

        Assert.That(ex.Message, Does.Contain("container"));
        var progress = service.GetProgress();
        Assert.That(progress.Stage, Is.EqualTo(UpdateInstallStage.Failed));
        Assert.That(progress.ErrorMessage, Does.Contain("container"));
    }

    [Test]
    public void StageAndInstallAsync_should_throw_and_set_failed_progress_when_containerized()
    {
        var service = new InstallUpdateService(
            _updateService,
            _updatePackageProvider,
            _postUpdateVerificationService,
            _appFolderInfo,
            installDirectory: _targetDir,
            isContainerizedOverride: true);

        var dummyPkg = Path.Combine(_tempDir, "package.zip");
        File.WriteAllText(dummyPkg, "dummy");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.StageAndInstallAsync(dummyPkg, targetVersion: "2.0.0");
        });

        Assert.That(ex.Message, Does.Contain("container"));
        Assert.That(service.GetProgress().Stage, Is.EqualTo(UpdateInstallStage.Failed));
    }

    [Test]
    public void StageAndInstallAsync_should_fail_when_sha256_verification_mismatches()
    {
        var service = new InstallUpdateService(
            _updateService,
            _updatePackageProvider,
            _postUpdateVerificationService,
            _appFolderInfo,
            installDirectory: _targetDir,
            isContainerizedOverride: false);

        var pkgPath = Path.Combine(_tempDir, "update_package.zip");
        File.WriteAllText(pkgPath, "some payload bytes for update");

        var invalidChecksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.StageAndInstallAsync(pkgPath, invalidChecksum, "2.0.0");
        });

        Assert.That(ex.Message, Does.Contain("SHA-256 verification failed"));
        var progress = service.GetProgress();
        Assert.That(progress.Stage, Is.EqualTo(UpdateInstallStage.Failed));
        Assert.That(progress.ErrorMessage, Does.Contain("SHA-256 verification failed"));
    }

    [Test]
    public async Task StageAndInstallAsync_should_stage_files_safely_handling_etxtbsy_via_rename()
    {
        var service = new InstallUpdateService(
            _updateService,
            _updatePackageProvider,
            _postUpdateVerificationService,
            _appFolderInfo,
            installDirectory: _targetDir,
            isContainerizedOverride: false);

        // Pre-create existing running binary in the target directory
        var existingBinary = Path.Combine(_targetDir, "Seedarr");
        await File.WriteAllTextAsync(existingBinary, "old-v1-executable-binary-content");

        // Create a zip archive containing the new binary
        var zipPath = Path.Combine(_tempDir, "seedarr-2.0.0.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("Seedarr");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("new-v2-executable-binary-content");
        }

        // Compute valid SHA-256 for the created zip
        var expectedSha256 = InstallUpdateService.ComputeSha256(zipPath);

        var result = await service.StageAndInstallAsync(zipPath, expectedSha256, "2.0.0");

        Assert.That(result, Is.True);
        var progress = service.GetProgress();
        Assert.That(progress.Stage, Is.EqualTo(UpdateInstallStage.RestartRequired));
        Assert.That(progress.Percentage, Is.EqualTo(100));

        // ETXTBSY avoidance: Old binary must have been moved to .old
        var oldBinary = Path.Combine(_targetDir, "Seedarr.old");
        Assert.That(File.Exists(oldBinary), Is.True);
        Assert.That(await File.ReadAllTextAsync(oldBinary), Is.EqualTo("old-v1-executable-binary-content"));

        // New binary must be present with new content
        Assert.That(File.Exists(existingBinary), Is.True);
        Assert.That(await File.ReadAllTextAsync(existingBinary), Is.EqualTo("new-v2-executable-binary-content"));

        // PostUpdateVerificationService must have been informed
        _postUpdateVerificationService.Received(1).StageUpdate("2.0.0", Arg.Any<string>());
    }

    [Test]
    public void SafelyReplaceFile_should_rename_existing_file_to_old_and_place_new_file()
    {
        var destFile = Path.Combine(_targetDir, "Seedarr");
        File.WriteAllText(destFile, "running-binary-v1");

        var srcFile = Path.Combine(_tempDir, "Seedarr-new");
        File.WriteAllText(srcFile, "running-binary-v2");

        InstallUpdateService.SafelyReplaceFile(srcFile, destFile);

        var oldFile = destFile + ".old";
        Assert.That(File.Exists(oldFile), Is.True);
        Assert.That(File.ReadAllText(oldFile), Is.EqualTo("running-binary-v1"));
        Assert.That(File.ReadAllText(destFile), Is.EqualTo("running-binary-v2"));
    }

    [TestCase("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "pkg.tar.gz", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [TestCase("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  pkg.tar.gz", "pkg.tar.gz", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [TestCase("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 *pkg.tar.gz", "pkg.tar.gz", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void ExtractChecksum_should_parse_various_checksum_file_formats(string content, string fileName, string expected)
    {
        var parsed = InstallUpdateService.ExtractChecksum(content, fileName);
        Assert.That(parsed, Is.EqualTo(expected));
    }
}
