using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Backup;
using Seedarr.Api.V1.Backup;

namespace NzbDrone.Core.Test.Backup;

[TestFixture]
public class BackupControllerTest
{
    private IBackupService _backupService;
    private BackupController _controller;

    [SetUp]
    public void SetUp()
    {
        _backupService = Substitute.For<IBackupService>();
        _controller = new BackupController(_backupService);
    }

    [Test]
    public void CreateBackup_when_successful_should_return_ok_with_backup_resource()
    {
        _backupService.CreateBackup(Arg.Any<BackupType>()).Returns(new BackupInfo
        {
            Name = "seedarr_backup_1.0_2026-01-01.zip",
            Path = "/path/to/backup.zip",
            Size = 1024,
            Time = DateTime.UtcNow
        });

        var result = _controller.CreateBackup();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        Assert.That(okResult.Value, Is.InstanceOf<BackupResource>());
        var resource = (BackupResource)okResult.Value;
        Assert.That(resource.Name, Is.EqualTo("seedarr_backup_1.0_2026-01-01.zip"));
        Assert.That(resource.Size, Is.EqualTo(1024));
    }

    [Test]
    public void CreateBackup_when_db_not_found_should_return_bad_request()
    {
        _backupService.CreateBackup(Arg.Any<BackupType>()).Returns((BackupInfo)null);

        var result = _controller.CreateBackup();

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("Database file not found"));
    }

    [Test]
    public void CreateBackup_when_insufficient_disk_space_should_return_bad_request()
    {
        _backupService.When(s => s.CreateBackup(Arg.Any<BackupType>()))
            .Do(_ => throw new InvalidOperationException("Insufficient disk space to create backup. Required: 500 MB, Available: 100 MB."));

        var result = _controller.CreateBackup();

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("Insufficient disk space to create backup"));
    }

    [Test]
    public void DeleteBackup_by_id_should_delete_backup_at_index()
    {
        _backupService.GetBackups().Returns(new List<BackupInfo>
        {
            new() { Name = "seedarr_backup_1.0_2026-01-01.zip", Size = 100, Time = DateTime.UtcNow },
            new() { Name = "seedarr_backup_1.0_2026-01-02.zip", Size = 200, Time = DateTime.UtcNow }
        });

        var result = _controller.DeleteBackup(2, null);

        Assert.That(result, Is.InstanceOf<OkResult>());
        _backupService.Received(1).DeleteBackup("seedarr_backup_1.0_2026-01-02.zip");
    }

    [Test]
    public void DeleteBackup_by_id_out_of_range_should_return_not_found()
    {
        _backupService.GetBackups().Returns(new List<BackupInfo>
        {
            new() { Name = "seedarr_backup_1.0_2026-01-01.zip", Size = 100, Time = DateTime.UtcNow }
        });

        var result = _controller.DeleteBackup(5, null);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
        _backupService.DidNotReceive().DeleteBackup(Arg.Any<string>());
    }

    [Test]
    public void DeleteBackup_by_id_zero_or_negative_should_return_not_found()
    {
        _backupService.GetBackups().Returns(new List<BackupInfo>
        {
            new() { Name = "seedarr_backup_1.0_2026-01-01.zip", Size = 100, Time = DateTime.UtcNow }
        });

        var result = _controller.DeleteBackup(0, null);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
        _backupService.DidNotReceive().DeleteBackup(Arg.Any<string>());
    }

    [Test]
    public void DeleteBackup_with_valid_fileName_should_delete_matching_backup()
    {
        _backupService.GetBackups().Returns(new List<BackupInfo>
        {
            new() { Name = "seedarr_backup_1.0_2026-01-01.zip", Size = 100, Time = DateTime.UtcNow },
            new() { Name = "seedarr_backup_1.0_2026-01-02.zip", Size = 200, Time = DateTime.UtcNow }
        });

        var result = _controller.DeleteBackup(0, "seedarr_backup_1.0_2026-01-01.zip");

        Assert.That(result, Is.InstanceOf<OkResult>());
        _backupService.Received(1).DeleteBackup("seedarr_backup_1.0_2026-01-01.zip");
    }

    [Test]
    public void DeleteBackup_with_nonexistent_fileName_should_return_not_found()
    {
        _backupService.GetBackups().Returns(new List<BackupInfo>
        {
            new() { Name = "seedarr_backup_1.0_2026-01-01.zip", Size = 100, Time = DateTime.UtcNow }
        });

        var result = _controller.DeleteBackup(0, "seedarr_backup_nonexistent.zip");

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
        _backupService.DidNotReceive().DeleteBackup(Arg.Any<string>());
    }

    [Test]
    public void DeleteBackup_with_path_traversal_fileName_should_return_bad_request()
    {
        _backupService.GetBackups().Returns(new List<BackupInfo>
        {
            new() { Name = "seedarr_backup_1.0_2026-01-01.zip", Size = 100, Time = DateTime.UtcNow }
        });

        var result = _controller.DeleteBackup(0, "../../etc/passwd.zip");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        _backupService.DidNotReceive().DeleteBackup(Arg.Any<string>());
    }

    [Test]
    public void DeleteBackup_with_invalid_extension_should_return_bad_request()
    {
        _backupService.GetBackups().Returns(new List<BackupInfo>
        {
            new() { Name = "seedarr_backup_1.0_2026-01-01.zip", Size = 100, Time = DateTime.UtcNow }
        });

        var result = _controller.DeleteBackup(0, "seedarr_backup_1.0_2026-01-01.tar.gz");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        _backupService.DidNotReceive().DeleteBackup(Arg.Any<string>());
    }

    [Test]
    public void RestoreBackup_with_valid_fileName_should_restore_backup_and_return_ok()
    {
        var request = new RestoreRequest { FileName = "seedarr_backup_1.0_2026-01-01.zip" };

        var result = _controller.RestoreBackup(request);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _backupService.Received(1).RestoreBackup("seedarr_backup_1.0_2026-01-01.zip");
    }

    [Test]
    public void RestoreBackup_with_missing_fileName_should_return_bad_request()
    {
        var request = new RestoreRequest { FileName = "" };

        var result = _controller.RestoreBackup(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        _backupService.DidNotReceive().RestoreBackup(Arg.Any<string>());
    }

    [Test]
    public void RestoreBackup_when_file_not_found_should_return_not_found()
    {
        var request = new RestoreRequest { FileName = "nonexistent.zip" };
        _backupService.When(s => s.RestoreBackup("nonexistent.zip"))
            .Do(_ => throw new FileNotFoundException("Backup file not found", "nonexistent.zip"));

        var result = _controller.RestoreBackup(request);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public void RestoreBackup_when_archive_is_corrupt_should_return_bad_request()
    {
        var request = new RestoreRequest { FileName = "corrupt.zip" };
        _backupService.When(s => s.RestoreBackup("corrupt.zip"))
            .Do(_ => throw new InvalidDataException("Corrupted zip archive"));

        var result = _controller.RestoreBackup(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void RestoreBackup_when_io_exception_should_return_bad_request()
    {
        var request = new RestoreRequest { FileName = "disk_full.zip" };
        _backupService.When(s => s.RestoreBackup("disk_full.zip"))
            .Do(_ => throw new IOException("Disk full"));

        var result = _controller.RestoreBackup(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public void RestoreBackup_when_invalid_operation_exception_should_return_bad_request()
    {
        var request = new RestoreRequest { FileName = "missing_db.zip" };
        _backupService.When(s => s.RestoreBackup("missing_db.zip"))
            .Do(_ => throw new InvalidOperationException("Database file not found in backup archive"));

        var result = _controller.RestoreBackup(request);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result;
        Assert.That(badRequest.Value?.ToString(), Does.Contain("Database file not found in backup archive"));
    }
}
