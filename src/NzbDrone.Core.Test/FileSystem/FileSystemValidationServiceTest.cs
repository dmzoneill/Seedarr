using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.FileSystem;

namespace NzbDrone.Core.Test.FileSystem;

[TestFixture]
public class FileSystemValidationServiceTest
{
    private FileSystemValidationService _subject;
    private string _tempDirectory;

    [SetUp]
    public void SetUp()
    {
        _subject = new FileSystemValidationService();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "seedarr_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup error
        }
    }

    [Test]
    public void ValidateDirectory_should_return_valid_for_existing_writable_directory()
    {
        var result = _subject.ValidateDirectory(_tempDirectory, testWrite: true);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.ErrorMessage, Is.Null);
        Assert.That(result.ResolvedPath, Is.EqualTo(Path.GetFullPath(_tempDirectory)));
    }

    [Test]
    public void ValidateDirectory_should_create_directory_if_it_does_not_exist_and_succeed()
    {
        var newSubDir = Path.Combine(_tempDirectory, "sub_directory");

        var result = _subject.ValidateDirectory(newSubDir, testWrite: true);

        Assert.That(result.IsValid, Is.True);
        Assert.That(Directory.Exists(newSubDir), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateDirectory_should_return_invalid_when_path_is_empty_or_whitespace(string path)
    {
        var result = _subject.ValidateDirectory(path, testWrite: true);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Path cannot be empty"));
    }

    [Test]
    public void ValidateDirectory_should_return_invalid_when_path_contains_invalid_characters()
    {
        var invalidPath = "test" + '\0' + "path";

        var result = _subject.ValidateDirectory(invalidPath, testWrite: true);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void ValidateDirectory_should_return_invalid_when_path_points_to_existing_file()
    {
        var tempFile = Path.Combine(_tempDirectory, "testfile.txt");
        File.WriteAllText(tempFile, "sample content");

        var result = _subject.ValidateDirectory(tempFile, testWrite: true);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("existing file"));
    }

    [Test]
    public void ValidateDirectory_should_return_error_result_when_directory_write_probes_unauthorized()
    {
        var service = new FileSystemValidationService(
            fileWriter: (p, b) => throw new UnauthorizedAccessException("Access denied"));

        var result = service.ValidateDirectory(_tempDirectory, testWrite: true);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Permission denied: process lacks write permissions to " + _tempDirectory));
    }

    [Test]
    public void ValidateDirectory_should_return_error_result_when_directory_write_probes_io_exception()
    {
        var service = new FileSystemValidationService(
            fileWriter: (p, b) => throw new IOException("Disk write fault"));

        var result = service.ValidateDirectory(_tempDirectory, testWrite: true);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Permission denied: process lacks write permissions to " + _tempDirectory));
    }

    [Test]
    public void ValidateDirectory_should_return_error_result_when_directory_creation_throws_unauthorized()
    {
        var nonExistentPath = Path.Combine(_tempDirectory, "forbidden_dir");
        var service = new FileSystemValidationService(
            directoryCreator: p => throw new UnauthorizedAccessException("Cannot create"));

        var result = service.ValidateDirectory(nonExistentPath, testWrite: false);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Permission denied: process lacks write permissions to " + nonExistentPath));
    }

    [Test]
    public void ValidateDirectory_should_skip_write_probe_when_testWrite_is_false()
    {
        var writeAttempted = false;
        var service = new FileSystemValidationService(
            fileWriter: (p, b) =>
            {
                writeAttempted = true;
                File.WriteAllBytes(p, b);
            });

        var result = service.ValidateDirectory(_tempDirectory, testWrite: false);

        Assert.That(result.IsValid, Is.True);
        Assert.That(writeAttempted, Is.False);
    }
}
