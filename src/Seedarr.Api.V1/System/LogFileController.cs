using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using global::System.IO;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("logfile")]
public class LogFileController : ControllerBase
{
    private readonly IAppFolderInfo _appFolderInfo;

    public LogFileController(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
    }

    [HttpGet]
    public ActionResult<List<LogFileResource>> GetLogFiles()
    {
        var logDir = Path.Combine(_appFolderInfo.AppDataFolder, "logs");

        if (!Directory.Exists(logDir))
        {
            return Ok(new List<LogFileResource>());
        }

        var files = Directory.GetFiles(logDir, "*.*", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new LogFileResource
            {
                Filename = f.Name,
                LastWriteTime = f.LastWriteTimeUtc,
                Size = f.Length
            })
            .ToList();

        return Ok(files);
    }

    [HttpGet("{filename}")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Filename is sanitized via Path.GetFileName and validated against the log directory")]
    public ActionResult GetLogFile(string filename)
    {
        var sanitized = Path.GetFileName(filename);

        if (string.IsNullOrWhiteSpace(sanitized) || sanitized != filename)
        {
            return BadRequest("Invalid filename");
        }

        var logDir = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "logs"));
        var logDirWithSep = logDir.EndsWith(Path.DirectorySeparatorChar)
            ? logDir
            : logDir + Path.DirectorySeparatorChar;

        var fullPath = Path.GetFullPath(Path.Combine(logDir, sanitized));

        if (!fullPath.StartsWith(logDirWithSep, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid filename");
        }

        if (!global::System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return File(stream, "text/plain", sanitized);
    }

    [HttpDelete]
    public ActionResult ClearLogFiles()
    {
        var logDir = Path.Combine(_appFolderInfo.AppDataFolder, "logs");

        if (!Directory.Exists(logDir))
        {
            return Ok();
        }

        var files = Directory.GetFiles(logDir, "*.*", SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);

                // Skip files that are currently active (less than 1 second old)
                if (info.Name == "seedarr.txt")
                {
                    continue;
                }

                global::System.IO.File.Delete(file);
            }
            catch (IOException)
            {
                // File is in use, skip it
            }
        }

        return Ok();
    }
}
