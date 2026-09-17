using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using Seedarr.Api.V1.System;

namespace NzbDrone.Core.Test.Controllers;

[TestFixture]
public class LogFileControllerTest
{
    private IAppFolderInfo _appFolderInfo;
    private string _tempDir;
    private string _logsDir;
    private LogFileController _controller;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seedarr_log_test_" + Guid.NewGuid().ToString("N"));
        _logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(_logsDir);

        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempDir);

        _controller = new LogFileController(_appFolderInfo);
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
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void GetLogFile_EnablesRangeProcessing()
    {
        var filePath = Path.Combine(_logsDir, "seedarr.txt");
        File.WriteAllText(filePath, "2026-09-16 12:00:00.0|INFO|App|Server started");

        var result = _controller.GetLogFile("seedarr.txt", download: false);

        Assert.That(result, Is.InstanceOf<FileStreamResult>());
        var fileStreamResult = (FileStreamResult)result;
        try
        {
            Assert.That(fileStreamResult.EnableRangeProcessing, Is.True);
            Assert.That(fileStreamResult.ContentType, Is.EqualTo("text/plain"));
        }
        finally
        {
            fileStreamResult.FileStream.Dispose();
        }
    }

    [Test]
    public void GetLogFile_WhenDownloadFalse_SetsFileDownloadNameToNullForInlineViewing()
    {
        var filePath = Path.Combine(_logsDir, "seedarr.txt");
        File.WriteAllText(filePath, "2026-09-16 12:00:00.0|INFO|App|Log content for inline viewing");

        var result = _controller.GetLogFile("seedarr.txt", download: false);

        Assert.That(result, Is.InstanceOf<FileStreamResult>());
        var fileStreamResult = (FileStreamResult)result;
        try
        {
            Assert.That(fileStreamResult.FileDownloadName, Is.Null.Or.Empty);
            Assert.That(fileStreamResult.EnableRangeProcessing, Is.True);
        }
        finally
        {
            fileStreamResult.FileStream.Dispose();
        }
    }

    [Test]
    public void GetLogFile_WhenDownloadTrue_SetsFileDownloadNameToTriggerAttachment()
    {
        var filePath = Path.Combine(_logsDir, "seedarr.txt");
        File.WriteAllText(filePath, "2026-09-16 12:00:00.0|INFO|App|Log content for download");

        var result = _controller.GetLogFile("seedarr.txt", download: true);

        Assert.That(result, Is.InstanceOf<FileStreamResult>());
        var fileStreamResult = (FileStreamResult)result;
        try
        {
            Assert.That(fileStreamResult.FileDownloadName, Is.EqualTo("seedarr.txt"));
            Assert.That(fileStreamResult.EnableRangeProcessing, Is.True);
        }
        finally
        {
            fileStreamResult.FileStream.Dispose();
        }
    }

    [Test]
    public void GetLogFile_WhenFileNotFound_ReturnsNotFound()
    {
        var result = _controller.GetLogFile("nonexistent.txt");

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [TestCase("../secret.txt")]
    [TestCase("..\\secret.txt")]
    [TestCase("")]
    [TestCase("   ")]
    public void GetLogFile_WhenInvalidFilename_ReturnsBadRequest(string invalidFilename)
    {
        var result = _controller.GetLogFile(invalidFilename);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void ClearLogFiles_ProtectsAllActiveLogFiles()
    {
        // Active files to protect
        var active1 = Path.Combine(_logsDir, "seedarr.txt");
        var active2 = Path.Combine(_logsDir, "seedarr.trace.txt");
        var active3 = Path.Combine(_logsDir, "seedarr.debug.txt");
        var active4 = Path.Combine(_logsDir, "seedarr.update.txt");

        // Archived / inactive files to delete
        var archived1 = Path.Combine(_logsDir, "seedarr_20260916_0.txt");
        var archived2 = Path.Combine(_logsDir, "seedarr.0.txt");
        var archived3 = Path.Combine(_logsDir, "seedarr.trace.20260915.txt");
        var oldFile = Path.Combine(_logsDir, "old_diagnostic.txt");

        File.WriteAllText(active1, "active log 1");
        File.WriteAllText(active2, "active log 2");
        File.WriteAllText(active3, "active log 3");
        File.WriteAllText(active4, "active log 4");
        File.WriteAllText(archived1, "archived 1");
        File.WriteAllText(archived2, "archived 2");
        File.WriteAllText(archived3, "archived 3");
        File.WriteAllText(oldFile, "old log");

        var result = _controller.ClearLogFiles();

        Assert.That(result, Is.InstanceOf<OkResult>());

        // Active files must still exist
        Assert.That(File.Exists(active1), Is.True, "seedarr.txt should be protected");
        Assert.That(File.Exists(active2), Is.True, "seedarr.trace.txt should be protected");
        Assert.That(File.Exists(active3), Is.True, "seedarr.debug.txt should be protected");
        Assert.That(File.Exists(active4), Is.True, "seedarr.update.txt should be protected");

        // Archived files must be deleted
        Assert.That(File.Exists(archived1), Is.False, "seedarr_20260916_0.txt should be deleted");
        Assert.That(File.Exists(archived2), Is.False, "seedarr.0.txt should be deleted");
        Assert.That(File.Exists(archived3), Is.False, "seedarr.trace.20260915.txt should be deleted");
        Assert.That(File.Exists(oldFile), Is.False, "old_diagnostic.txt should be deleted");
    }

    [TestCase("seedarr.txt", true)]
    [TestCase("seedarr.trace.txt", true)]
    [TestCase("seedarr.debug.txt", true)]
    [TestCase("seedarr.update.txt", true)]
    [TestCase("SEEDARR.TXT", true)]
    [TestCase("seedarr_20260916_0.txt", false)]
    [TestCase("seedarr.0.txt", false)]
    [TestCase("seedarr.1.txt", false)]
    [TestCase("seedarr.trace.20260915.txt", false)]
    [TestCase("other.txt", false)]
    public void IsActiveLogFile_ClassifiesCorrectly(string filename, bool expected)
    {
        Assert.That(LogFileController.IsActiveLogFile(filename), Is.EqualTo(expected));
    }

    [Test]
    public void ClearLogFiles_WhenDirectoryDoesNotExist_ReturnsOk()
    {
        _appFolderInfo.AppDataFolder.Returns(Path.Combine(_tempDir, "nonexistent_dir"));

        var result = _controller.ClearLogFiles();

        Assert.That(result, Is.InstanceOf<OkResult>());
    }

    [Test]
    public void ClearLogFiles_HandlesUnauthorizedAccessExceptionGracefully()
    {
        var lockedFile = Path.Combine(_logsDir, "readonly_archive.txt");
        File.WriteAllText(lockedFile, "readonly archive content");

        // Lock file by keeping write lock open so delete throws IOException/UnauthorizedAccessException
        using var stream = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = _controller.ClearLogFiles();

        Assert.That(result, Is.InstanceOf<OkResult>());
    }
}
