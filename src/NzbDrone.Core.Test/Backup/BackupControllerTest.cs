using System;
using System.Collections.Generic;
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
}
