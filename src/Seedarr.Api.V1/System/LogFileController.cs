using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using Seedarr.Http;

namespace Seedarr.Api.V1.System;

[V1ApiController("logfile")]
public class LogFileController : ControllerBase
{
    private static readonly HashSet<string> ActiveLogFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "seedarr.txt",
        "seedarr.trace.txt",
        "seedarr.debug.txt",
        "seedarr.update.txt",
    };

    private readonly IAppFolderInfo _appFolderInfo;

    public LogFileController(IAppFolderInfo appFolderInfo)
    {
        _appFolderInfo = appFolderInfo;
    }

    public static bool IsActiveLogFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (ActiveLogFileNames.Contains(fileName))
        {
            return true;
        }

        if (fileName.StartsWith("seedarr.", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var middle = fileName.Substring("seedarr.".Length, fileName.Length - "seedarr.".Length - ".txt".Length);
            if (middle.Length > 0 && middle.All(char.IsLetter))
            {
                return true;
            }
        }

        return false;
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
                Size = f.Length,
            })
            .ToList();

        return Ok(files);
    }

    [HttpGet("{filename}")]
    [HttpGet("/api/v1/log/file/{filename}")]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Filename is sanitized via Path.GetFileName and validated against the log directory")]
    public ActionResult GetLogFile(string filename, [FromQuery] bool download = false)
    {
        if (string.IsNullOrWhiteSpace(filename) ||
            filename.Contains('/') ||
            filename.Contains('\\') ||
            filename.Contains(".."))
        {
            return BadRequest("Invalid filename");
        }

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
        return new FileStreamResult(stream, "text/plain")
        {
            FileDownloadName = download ? sanitized : null,
            EnableRangeProcessing = true,
        };
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

                if (IsActiveLogFile(info.Name))
                {
                    continue;
                }

                global::System.IO.File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // File is in use or inaccessible, skip it
            }
        }

        return Ok();
    }
}
