using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using NzbDrone.Core.Packages;
using NzbDrone.Core.Torrents;
using Seedarr.Http;

namespace Seedarr.Api.V1.Packages;

[V1ApiController("packages")]
public class PackageController : ControllerBase
{
    private static readonly HashSet<char> InvalidFileNameChars = new(
        Path.GetInvalidFileNameChars()
            .Concat(new[] { '\"', '<', '>', '|', ':', '*', '?', '\\', '/', ';' }));

    private readonly IPackageExportService _packageExportService;
    private readonly ITorrentService _torrentService;
    private readonly IPackageImportService _packageImportService;

    public PackageController(
        IPackageExportService packageExportService,
        ITorrentService torrentService,
        IPackageImportService packageImportService = null)
    {
        _packageExportService = packageExportService ?? throw new ArgumentNullException(nameof(packageExportService));
        _torrentService = torrentService ?? throw new ArgumentNullException(nameof(torrentService));
        _packageImportService = packageImportService;
    }

    [HttpGet("export")]
    [HttpGet("/api/v1/package/export")]
    public async Task<IActionResult> Export(
        [FromQuery] string torrentIds = null,
        [FromQuery] bool includePayload = false)
    {
        var idList = ParseTorrentIds(torrentIds, Request?.Query?["torrentIds"] ?? StringValues.Empty);
        if (idList.Count == 0)
        {
            return BadRequest(new { message = "At least one valid torrent ID must be provided." });
        }

        var existingTorrents = new List<Torrent>();
        foreach (var id in idList)
        {
            var torrent = _torrentService.Get(id);
            if (torrent != null)
            {
                existingTorrents.Add(torrent);
            }
        }

        if (existingTorrents.Count == 0)
        {
            return NotFound(new { message = "None of the specified torrents were found." });
        }

        var filename = DeterminePackageFileName(existingTorrents);

        var outputStream = Response?.Body;
        if (Response != null)
        {
            Response.ContentType = "application/x-seedarr-package";
            Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
        }

        var cancellationToken = HttpContext?.RequestAborted ?? default;
        await _packageExportService.ExportPackageAsync(
            outputStream ?? Stream.Null,
            existingTorrents.Select(t => t.Id),
            includePayload,
            cancellationToken);

        return new EmptyResult();
    }

    [HttpPost("import")]
    [HttpPost("/api/v1/package/import")]
    public async Task<IActionResult> Import(
        IFormFile file = null,
        [FromQuery] string destinationPath = null)
    {
        if (_packageImportService == null)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, new { message = "Package import service is not available." });
        }

        if (file == null && Request?.HasFormContentType == true && Request.Form.Files.Count > 0)
        {
            file = Request.Form.Files[0];
        }

        Stream archiveStream = null;
        if (file != null && file.Length > 0)
        {
            archiveStream = file.OpenReadStream();
        }
        else if (Request?.Body != null && (Request.ContentLength.GetValueOrDefault() > 0 || Request.Body.CanRead))
        {
            archiveStream = Request.Body;
        }

        if (archiveStream == null || (archiveStream.CanSeek && archiveStream.Length == 0))
        {
            return BadRequest(new { message = "No package archive file provided." });
        }

        try
        {
            var options = new PackageImportOptions
            {
                DestinationPath = destinationPath
            };

            var cancellationToken = HttpContext?.RequestAborted ?? default;
            var result = await _packageImportService.ImportPackageAsync(archiveStream, options, cancellationToken);
            return Ok(result);
        }
        catch (SecurityException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    public static List<int> ParseTorrentIds(string torrentIdsParam, StringValues queryValues)
    {
        var result = new HashSet<int>();

        if (!string.IsNullOrWhiteSpace(torrentIdsParam))
        {
            foreach (var part in torrentIdsParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var id) && id > 0)
                {
                    result.Add(id);
                }
            }
        }

        foreach (var val in queryValues)
        {
            if (string.IsNullOrWhiteSpace(val))
            {
                continue;
            }

            foreach (var part in val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var id) && id > 0)
                {
                    result.Add(id);
                }
            }
        }

        return result.ToList();
    }

    public static string DeterminePackageFileName(IReadOnlyList<Torrent> torrents)
    {
        if (torrents == null || torrents.Count == 0)
        {
            return "package.seedarr";
        }

        if (torrents.Count == 1)
        {
            var sanitized = SanitizeFileName(torrents[0].Name);
            return string.IsNullOrWhiteSpace(sanitized) ? "package.seedarr" : $"{sanitized}.seedarr";
        }

        return "package.seedarr";
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);

        foreach (var ch in name)
        {
            if (!InvalidFileNameChars.Contains(ch) && ch >= 32)
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }

        return sb.ToString().Trim(' ', '.', '_');
    }
}
