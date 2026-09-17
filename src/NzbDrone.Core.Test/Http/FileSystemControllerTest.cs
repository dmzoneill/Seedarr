using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Seedarr.Api.V1.FileSystem;

namespace NzbDrone.Core.Test.Http;

[TestFixture]
public class FileSystemControllerTest
{
    private FileSystemController _controller;

    [SetUp]
    public void SetUp()
    {
        _controller = new FileSystemController();
    }

    [Test]
    public void GetRootListing_Returns_Drives_On_Unix_With_Drive_Type_Path_And_Size()
    {
        var result = FileSystemController.GetRootListing();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Directories, Is.Not.Null);
        Assert.That(result.Directories.Count, Is.GreaterThan(0));

        var driveEntries = result.Directories.Where(d => d.Type == "drive").ToList();
        Assert.That(driveEntries, Is.Not.Empty);

        foreach (var drive in driveEntries)
        {
            Assert.That(drive.Path, Is.Not.Null.And.Not.Empty);
            Assert.That(drive.Name, Is.Not.Null.And.Not.Empty);
        }

        if (!OperatingSystem.IsWindows())
        {
            var rootDrive = driveEntries.FirstOrDefault(d => d.Path == "/");
            Assert.That(rootDrive, Is.Not.Null);
            Assert.That(rootDrive.Size, Is.Not.Null);
            Assert.That(rootDrive.Size.Value, Is.GreaterThan(0));
            Assert.That(rootDrive.FreeSpace, Is.Not.Null);
        }
    }

    [Test]
    public void GetContents_Root_Returns_Ok_With_Drives()
    {
        var actionResult = _controller.GetContents(null);

        Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)actionResult.Result;
        Assert.That(okResult.Value, Is.InstanceOf<FileSystemResource>());

        var resource = (FileSystemResource)okResult.Value;
        Assert.That(resource.Directories.Any(d => d.Type == "drive"), Is.True);
    }

    [Test]
    public void GetContents_Does_Not_Throw_When_Restricted_Directories_Exist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_test_perm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var normalSubDir = Path.Combine(tempDir, "accessible");
        Directory.CreateDirectory(normalSubDir);

        var restrictedSubDir = Path.Combine(tempDir, "restricted");
        Directory.CreateDirectory(restrictedSubDir);

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(restrictedSubDir, UnixFileMode.None);
            }

            var actionResult = _controller.GetContents(tempDir);

            Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
            var okResult = (OkObjectResult)actionResult.Result;
            var resource = (FileSystemResource)okResult.Value;

            Assert.That(resource.Directories.Any(d => d.Name == "accessible"), Is.True);
        }
        finally
        {
            try
            {
                if (!OperatingSystem.IsWindows() && Directory.Exists(restrictedSubDir))
                {
                    File.SetUnixFileMode(restrictedSubDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }

                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Cleanup best effort
            }
        }
    }

    [Test]
    public void GetContents_NonExistentPath_Returns_NotFound()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "seedarr_nonexistent_" + Guid.NewGuid().ToString("N"));
        var actionResult = _controller.GetContents(nonExistent);

        Assert.That(actionResult.Result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void GetContents_InvalidPath_Returns_BadRequest()
    {
        var actionResult = _controller.GetContents("path\0withnull");

        Assert.That(actionResult.Result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void GetContents_WithPagination_Returns_Paged_Directories_And_Sets_Metadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_test_page_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var i = 0; i < 10; i++)
            {
                Directory.CreateDirectory(Path.Combine(tempDir, $"dir{i:D2}"));
            }

            var actionResult = _controller.GetContents(tempDir, includeFiles: false, skip: 2, take: 3);

            Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
            var okResult = (OkObjectResult)actionResult.Result;
            var resource = (FileSystemResource)okResult.Value;

            Assert.That(resource.Directories, Has.Count.EqualTo(3));
            Assert.That(resource.Directories[0].Name, Is.EqualTo("dir02"));
            Assert.That(resource.Directories[1].Name, Is.EqualTo("dir03"));
            Assert.That(resource.Directories[2].Name, Is.EqualTo("dir04"));
            Assert.That(resource.TotalDirectories, Is.EqualTo(10));
            Assert.That(resource.TotalFiles, Is.EqualTo(0));
            Assert.That(resource.IsTruncated, Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void GetContents_WhenAllItemsFitInPage_IsTruncated_Is_False()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_test_fit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var i = 0; i < 5; i++)
            {
                Directory.CreateDirectory(Path.Combine(tempDir, $"dir{i:D2}"));
            }

            var actionResult = _controller.GetContents(tempDir, includeFiles: false, skip: 0, take: 10);

            Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
            var okResult = (OkObjectResult)actionResult.Result;
            var resource = (FileSystemResource)okResult.Value;

            Assert.That(resource.Directories, Has.Count.EqualTo(5));
            Assert.That(resource.TotalDirectories, Is.EqualTo(5));
            Assert.That(resource.IsTruncated, Is.False);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void GetContents_WithFilesPagination_Sets_TotalFiles_And_IsTruncated()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_test_files_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var i = 0; i < 5; i++)
            {
                File.WriteAllText(Path.Combine(tempDir, $"file{i:D2}.txt"), "test");
            }

            var actionResult = _controller.GetContents(tempDir, includeFiles: true, skip: 1, take: 2);

            Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
            var okResult = (OkObjectResult)actionResult.Result;
            var resource = (FileSystemResource)okResult.Value;

            Assert.That(resource.Files, Has.Count.EqualTo(2));
            Assert.That(resource.Files[0].Name, Is.EqualTo("file01.txt"));
            Assert.That(resource.Files[1].Name, Is.EqualTo("file02.txt"));
            Assert.That(resource.TotalFiles, Is.EqualTo(5));
            Assert.That(resource.IsTruncated, Is.True);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void GetContents_DefaultPaginationLimits_Clamps_Invalid_Values()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "seedarr_test_clamp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            for (var i = 0; i < 3; i++)
            {
                Directory.CreateDirectory(Path.Combine(tempDir, $"dir{i:D2}"));
            }

            // negative skip and non-positive take should clamp to skip: 0, take: 500
            var actionResult = _controller.GetContents(tempDir, includeFiles: false, skip: -10, take: -5);

            Assert.That(actionResult.Result, Is.InstanceOf<OkObjectResult>());
            var okResult = (OkObjectResult)actionResult.Result;
            var resource = (FileSystemResource)okResult.Value;

            Assert.That(resource.Directories, Has.Count.EqualTo(3));
            Assert.That(resource.TotalDirectories, Is.EqualTo(3));
            Assert.That(resource.IsTruncated, Is.False);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
