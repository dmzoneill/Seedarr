using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Backup;
using Seedarr.Http;

namespace Seedarr.Api.V1.Backup;

[V1ApiController("backup")]
public class BackupController : Controller
{
    private readonly IBackupService _backupService;

    public BackupController(IBackupService backupService)
    {
        _backupService = backupService;
    }

    [HttpGet]
    public ActionResult<List<BackupResource>> GetBackups()
    {
        var backups = _backupService.GetBackups();

        return backups.Select((b, i) => new BackupResource
        {
            Id = i + 1,
            Name = b.Name,
            Size = b.Size,
            Time = b.Time
        }).ToList();
    }

    [HttpPost]
    public ActionResult<BackupResource> CreateBackup()
    {
        var backup = _backupService.CreateBackup();

        if (backup == null)
        {
            return BadRequest(new { message = "Database file not found, cannot create backup" });
        }

        return Ok(new BackupResource
        {
            Id = 1,
            Name = backup.Name,
            Size = backup.Size,
            Time = backup.Time
        });
    }

    [HttpDelete("{id:int}")]
    public ActionResult DeleteBackup(int id, [FromQuery] string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            var backups = _backupService.GetBackups();

            if (id < 1 || id > backups.Count)
            {
                return NotFound();
            }

            fileName = backups[id - 1].Name;
        }

        var safeFileName = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Invalid backup file name" });
        }

        _backupService.DeleteBackup(safeFileName);
        return Ok();
    }

    [HttpGet("{id:int}/download")]
    public ActionResult DownloadBackup(int id)
    {
        var backups = _backupService.GetBackups();

        if (id < 1 || id > backups.Count)
        {
            return NotFound();
        }

        var backup = backups[id - 1];
        var stream = _backupService.GetBackupStream(backup.Name);

        if (stream == null)
        {
            return NotFound();
        }

        return File(stream, "application/zip", backup.Name);
    }

    [HttpPost("restore")]
    public ActionResult RestoreBackup([FromBody] RestoreRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.FileName))
        {
            return BadRequest(new { message = "fileName is required" });
        }

        var safeFileName = Path.GetFileName(request.FileName);

        if (string.IsNullOrWhiteSpace(safeFileName) || !safeFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Invalid backup file name" });
        }

        _backupService.RestoreBackup(safeFileName);
        return Ok(new { message = "Backup restored. Restart required." });
    }
}

public class RestoreRequest
{
    public string FileName { get; set; }
}
