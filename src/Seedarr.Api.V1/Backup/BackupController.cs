using System.Collections.Generic;
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
    public ActionResult<List<BackupInfo>> GetBackups() => _backupService.GetBackups();

    [HttpPost]
    public ActionResult<object> CreateBackup()
    {
        var fileName = _backupService.CreateBackup();
        return Ok(new { fileName });
    }

    [HttpDelete("{fileName}")]
    public ActionResult DeleteBackup(string fileName)
    {
        _backupService.DeleteBackup(fileName);
        return Ok();
    }
}
