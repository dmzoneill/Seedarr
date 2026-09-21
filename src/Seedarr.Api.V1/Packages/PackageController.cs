using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using NzbDrone.Core.Packages;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Torrents.Package;
using Seedarr.Http;

namespace Seedarr.Api.V1.Packages;

public class PackageExportRequest
{
    public List<int> TorrentIds { get; set; } = new();
    public bool IncludePayload { get; set; }
}

[V1ApiController("packages")]
public class PackageController : ControllerBase
{
    private static readonly HashSet<char> InvalidFileNameChars = new(
        Path.GetInvalidFileNameChars()
            .Concat(new[] { '\"', '<', '>', '|', ':', '*', '?', '\\', '/', ';' }));

    private readonly IPackageExportService _packageExportService;
    private readonly ITorrentService _torrentService;
    private readonly IPackageImportService _packageImportService;
    private readonly ITorrentPackageService _torrentPackageService;

    public PackageController(
        IPackageExportService packageExportService,
        ITorrentService torrentService,
        IPackageImportService packageImportService = null,
        ITorrentPackageService torrentPackageService = null)
    {
        _packageExportService = packageExportService;
        _torrentService = torrentService ?? throw new ArgumentNullException(nameof(torrentService));
        _packageImportService = packageImportService;
        _torrentPackageService = torrentPackageService;
    }

    public PackageController(
        ITorrentPackageService torrentPackageService,
        ITorrentService torrentService)
        : this(null, torrentService, null, torrentPackageService)
    {
    }

    [HttpGet("export")]
    [HttpGet("/api/v1/package/export")]
    public async Task<IActionResult> Export(
        [FromQuery] string torrentIds = null,
        [FromQuery] bool includePayload = false)
    {
        var idList = ParseTorrentIds(torrentIds, Request?.Query?["torrentIds"] ?? StringValues.Empty);
        return await ExecuteExportAsync(idList, includePayload);
    }

    [HttpPost("export")]
    [HttpPost("/api/v1/package/export")]
    public async Task<IActionResult> ExportPost(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PackageExportRequest request = null,
        [FromQuery] string torrentIds = null,
        [FromQuery] bool? includePayload = null)
    {
        var idList = new List<int>();
        if (request?.TorrentIds != null && request.TorrentIds.Count > 0)
        {
            idList.AddRange(request.TorrentIds.Where(id => id > 0));
        }

        if (idList.Count == 0)
        {
            idList = ParseTorrentIds(torrentIds, Request?.Query?["torrentIds"] ?? StringValues.Empty);
        }

        var payload = request?.IncludePayload ?? includePayload ?? false;
        return await ExecuteExportAsync(idList, payload);
    }

    private async Task<IActionResult> ExecuteExportAsync(List<int> idList, bool includePayload)
    {
        if (idList.Count == 0)
        {
            return BadRequest(new { message = "At least one valid torrent ID must be provided." });
        }

        if (_torrentPackageService == null && _packageExportService == null)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, new { message = "Package export service is not available." });
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
        var exportTarget = outputStream ?? Stream.Null;
        var torrentIdList = existingTorrents.Select(t => t.Id).ToList();

        if (_torrentPackageService != null)
        {
            await _torrentPackageService.ExportPackageAsync(
                exportTarget,
                torrentIdList,
                includePayload,
                cancellationToken);
        }
        else
        {
            await _packageExportService.ExportPackageAsync(
                exportTarget,
                torrentIdList,
                includePayload,
                cancellationToken);
        }

        return new EmptyResult();
    }

    [HttpPost("import")]
    [HttpPost("/api/v1/package/import")]
    public async Task<IActionResult> Import(
        IFormFile file = null,
        [FromQuery] string destinationPath = null)
    {
        if (_torrentPackageService == null && _packageImportService == null)
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
            PackageImportResult result;

            if (_torrentPackageService != null)
            {
                result = await _torrentPackageService.ImportPackageAsync(archiveStream, options, cancellationToken);
            }
            else
            {
                result = await _packageImportService.ImportPackageAsync(archiveStream, options, cancellationToken);
            }

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
