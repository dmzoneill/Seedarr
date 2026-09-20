using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.MediaEnrichment;
using Seedarr.Http;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.MediaCover;

[V1ApiController("mediacover")]
[SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
public class MediaCoverController : RestController<MediaMetadataResource>
{
    private readonly IMediaEnrichmentService _mediaEnrichmentService;
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly Logger _logger;

    private static readonly HashSet<string> ValidArtworkTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "poster",
        "backdrop",
        "banner",
        "fanart",
        "logo",
        "clearart",
        "thumb",
        "screenshot",
    };

    public MediaCoverController(
        IMediaEnrichmentService mediaEnrichmentService,
        IAppFolderInfo appFolderInfo = null)
    {
        _mediaEnrichmentService = mediaEnrichmentService;
        _appFolderInfo = appFolderInfo;
        _logger = LogManager.GetCurrentClassLogger();
    }

    [HttpGet]
    public ActionResult<List<MediaMetadataResource>> GetAll()
    {
        var all = _mediaEnrichmentService.GetAllMetadata();
        return Ok(all.Values.Select(MediaMetadataResourceMapper.ToResource).ToList());
    }

    [HttpDelete("{torrentId:int}")]
    public IActionResult Delete(int torrentId)
    {
        _mediaEnrichmentService.DeleteMetadata(torrentId);
        return NoContent();
    }

    [HttpGet("{torrentId:int}")]
    public ActionResult<MediaMetadataResource> GetByTorrentId(int torrentId)
    {
        var meta = _mediaEnrichmentService.GetMetadata(torrentId);
        if (meta == null)
        {
            return NotFound();
        }

        return Ok(MediaMetadataResourceMapper.ToResource(meta));
    }

    [HttpGet("{torrentId:int}/poster.jpg")]
    [HttpGet("{torrentId:int}/poster")]
    [AllowAnonymous]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    public ActionResult GetPoster(int torrentId)
    {
        return ServeArtwork(torrentId, "poster");
    }

    [HttpGet("{torrentId:int}/backdrop.jpg")]
    [HttpGet("{torrentId:int}/backdrop")]
    [HttpGet("{torrentId:int}/fanart.jpg")]
    [HttpGet("{torrentId:int}/fanart")]
    [AllowAnonymous]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    public ActionResult GetBackdrop(int torrentId)
    {
        return ServeArtwork(torrentId, "backdrop");
    }

    [HttpGet("{torrentId:int}/banner.jpg")]
    [HttpGet("{torrentId:int}/banner")]
    [AllowAnonymous]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    public ActionResult GetBanner(int torrentId)
    {
        return ServeArtwork(torrentId, "banner");
    }

    [HttpGet("{torrentId:int}/{filename}")]
    [AllowAnonymous]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    public ActionResult GetCover(int torrentId, string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename.Contains("..") || filename.Contains('/') || filename.Contains('\\'))
        {
            return NotFound();
        }

        var type = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrWhiteSpace(type))
        {
            type = filename;
        }

        return ServeArtwork(torrentId, type);
    }

    [HttpGet("placeholder.svg")]
    [HttpGet("placeholder")]
    [AllowAnonymous]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Placeholder is dynamically generated in memory")]
    public ActionResult GetPlaceholder(
        [FromQuery] string title = null,
        [FromQuery] string category = null,
        [FromQuery] int width = 300,
        [FromQuery] int height = 450)
    {
        if (width <= 0)
        {
            width = 300;
        }

        if (height <= 0)
        {
            height = 450;
        }

        var svg = GeneratePlaceholderSvg(title, category, width, height);

        if (Response != null)
        {
            Response.Headers["Cache-Control"] = "public, max-age=2592000, immutable";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        }

        return Content(svg, "image/svg+xml", Encoding.UTF8);
    }

    [HttpGet("{torrentId:int}/placeholder.svg")]
    [HttpGet("{torrentId:int}/placeholder")]
    [AllowAnonymous]
    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Placeholder is dynamically generated in memory")]
    public ActionResult GetPlaceholder(int torrentId)
    {
        if (torrentId <= 0)
        {
            return NotFound();
        }

        var meta = _mediaEnrichmentService.GetMetadata(torrentId);
        var title = meta?.Title ?? $"Torrent #{torrentId}";
        var svg = GeneratePlaceholderSvg(title, meta?.ArrType);

        if (Response != null)
        {
            Response.Headers["Cache-Control"] = "public, max-age=2592000, immutable";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        }

        return Content(svg, "image/svg+xml", Encoding.UTF8);
    }

    public static string GeneratePlaceholderSvg(string title, string category = null)
    {
        return GeneratePlaceholderSvg(title, category, 300, 450);
    }

    public static string GeneratePlaceholderSvg(string title, string category, int width, int height)
    {
        if (width <= 0)
        {
            width = 300;
        }

        if (height <= 0)
        {
            height = 450;
        }

        var safeTitle = !string.IsNullOrWhiteSpace(title) ? SecurityElement.Escape(title.Trim()) : string.Empty;
        var safeCategory = !string.IsNullOrWhiteSpace(category) ? SecurityElement.Escape(category.Trim()) : string.Empty;
        var icon = GetCategoryIcon(category);
        var initials = GetInitials(title);

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        sb.Append("<defs>");
        sb.Append("<linearGradient id=\"bgGrad\" x1=\"0%\" y1=\"0%\" x2=\"100%\" y2=\"100%\">");
        sb.Append("<stop offset=\"0%\" stop-color=\"#2e2a24\"/>");
        sb.Append("<stop offset=\"50%\" stop-color=\"#1c1a17\"/>");
        sb.Append("<stop offset=\"100%\" stop-color=\"#121110\"/>");
        sb.Append("</linearGradient>");
        sb.Append("<radialGradient id=\"accentGlow\" cx=\"50%\" cy=\"35%\" r=\"50%\">");
        sb.Append("<stop offset=\"0%\" stop-color=\"#c8a84e\" stop-opacity=\"0.18\"/>");
        sb.Append("<stop offset=\"100%\" stop-color=\"#c8a84e\" stop-opacity=\"0\"/>");
        sb.Append("</radialGradient>");
        sb.Append("</defs>");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"url(#bgGrad)\"/>");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"url(#accentGlow)\"/>");
        sb.Append($"<rect x=\"10\" y=\"10\" width=\"{width - 20}\" height=\"{height - 20}\" rx=\"8\" fill=\"none\" stroke=\"#38332b\" stroke-width=\"1.5\" stroke-dasharray=\"4 4\" opacity=\"0.6\"/>");
        sb.Append($"<text x=\"50%\" y=\"38%\" font-size=\"48\" text-anchor=\"middle\" dominant-baseline=\"central\">{icon}</text>");

        if (!string.IsNullOrEmpty(initials))
        {
            sb.Append("<circle cx=\"50%\" cy=\"52%\" r=\"18\" fill=\"#2a2620\" stroke=\"#c8a84e\" stroke-width=\"1.5\" stroke-opacity=\"0.6\"/>");
            sb.Append($"<text x=\"50%\" y=\"52%\" font-family=\"system-ui, -apple-system, sans-serif\" font-size=\"13\" font-weight=\"bold\" fill=\"#c8a84e\" text-anchor=\"middle\" dominant-baseline=\"central\">{initials}</text>");
        }

        if (!string.IsNullOrEmpty(safeTitle))
        {
            var titleY = string.IsNullOrEmpty(initials) ? "55%" : "64%";
            sb.Append($"<text x=\"50%\" y=\"{titleY}\" font-family=\"system-ui, -apple-system, sans-serif\" font-size=\"14\" font-weight=\"600\" fill=\"#ede8de\" text-anchor=\"middle\" dominant-baseline=\"central\">{safeTitle}</text>");
        }

        if (!string.IsNullOrEmpty(safeCategory))
        {
            var catY = string.IsNullOrEmpty(initials) && string.IsNullOrEmpty(safeTitle) ? "50%" : "72%";
            sb.Append($"<text x=\"50%\" y=\"{catY}\" font-family=\"system-ui, -apple-system, sans-serif\" font-size=\"11\" font-weight=\"500\" fill=\"#9c9484\" text-anchor=\"middle\" dominant-baseline=\"central\" letter-spacing=\"1\">{safeCategory}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string GetCategoryIcon(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return "📦";
        }

        var cat = category.Trim().ToLowerInvariant();
        if (cat.Contains("movie") || cat.Contains("radarr") || cat.Contains("film") || cat.Contains("cinema"))
        {
            return "🎬";
        }

        if (cat.Contains("tv") || cat.Contains("sonarr") || cat.Contains("series") || cat.Contains("show") || cat.Contains("episode"))
        {
            return "📺";
        }

        if (cat.Contains("music") || cat.Contains("audio") || cat.Contains("lidarr") || cat.Contains("song") || cat.Contains("album"))
        {
            return "🎵";
        }

        return "📦";
    }

    private static string GetInitials(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var parts = title.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var initials = new StringBuilder();
        foreach (var p in parts)
        {
            if (char.IsLetterOrDigit(p[0]))
            {
                initials.Append(char.ToUpperInvariant(p[0]));
                if (initials.Length >= 3)
                {
                    break;
                }
            }
        }

        return initials.ToString();
    }

    private ActionResult ServeMissingArtworkFallback(int torrentId, TorrentMediaMetadata meta, string type)
    {
        var title = meta?.Title ?? $"Torrent #{torrentId}";
        var category = meta?.ArrType;
        var isBackdropOrFanart = string.Equals(type, "backdrop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "fanart", StringComparison.OrdinalIgnoreCase);
        var isBanner = string.Equals(type, "banner", StringComparison.OrdinalIgnoreCase);

        var width = isBackdropOrFanart
            ? 800
            : isBanner
                ? 750
                : 300;
        var height = isBackdropOrFanart
            ? 450
            : isBanner
                ? 140
                : 450;

        var svg = GeneratePlaceholderSvg(title, category, width, height);

        if (Response != null)
        {
            Response.Headers["Cache-Control"] = "public, max-age=2592000, immutable";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        }

        return Content(svg, "image/svg+xml", Encoding.UTF8);
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    private ActionResult ServeArtwork(int torrentId, string type)
    {
        if (torrentId <= 0 || string.IsNullOrWhiteSpace(type) || !ValidArtworkTypes.Contains(type))
        {
            return NotFound();
        }

        var meta = _mediaEnrichmentService.GetMetadata(torrentId);

        string path = null;
        if (string.Equals(type, "poster", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "thumb", StringComparison.OrdinalIgnoreCase))
        {
            path = meta?.PosterLocalPath;
        }
        else if (string.Equals(type, "backdrop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "fanart", StringComparison.OrdinalIgnoreCase))
        {
            path = meta?.BackdropLocalPath;
        }

        if (!string.IsNullOrEmpty(path))
        {
            if (path.Contains("..") || !IsPathAllowed(path, out var fullCandidatePath))
            {
                if (path.Contains(".."))
                {
                    _logger.Warn("Media cover artwork path contains directory traversal characters: {0}", path);
                }

                return NotFound();
            }

            path = fullCandidatePath;
        }

        if (string.IsNullOrEmpty(path) || !global::System.IO.File.Exists(path))
        {
            path = FindArtworkOnDisk(torrentId, meta, type);
        }

        if (string.IsNullOrEmpty(path))
        {
            return ServeMissingArtworkFallback(torrentId, meta, type);
        }

        if (!IsPathAllowed(path, out var fullPath))
        {
            return NotFound();
        }

        if (!global::System.IO.File.Exists(fullPath))
        {
            return ServeMissingArtworkFallback(torrentId, meta, type);
        }

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };

        try
        {
            global::System.IO.File.SetLastAccessTimeUtc(fullPath, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to update last access time for {0}", fullPath);
        }

        var fileInfo = new FileInfo(fullPath);
        var lastModifiedUtc = fileInfo.LastWriteTimeUtc;
        var etag = $"\"{lastModifiedUtc.Ticks:x}-{fileInfo.Length:x}\"";

        if (Response != null)
        {
            Response.Headers["Cache-Control"] = "public, max-age=604800, must-revalidate";
            Response.Headers["ETag"] = etag;
            Response.Headers["Last-Modified"] = lastModifiedUtc.ToString("R");
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
            }
        }

        if (Request != null)
        {
            var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch))
            {
                if (ifNoneMatch == "*" ||
                    string.Equals(ifNoneMatch, etag, StringComparison.OrdinalIgnoreCase) ||
                    ifNoneMatch.Split(',').Select(t => t.Trim()).Any(t => string.Equals(t, etag, StringComparison.OrdinalIgnoreCase) || string.Equals(t, $"W/{etag}", StringComparison.OrdinalIgnoreCase)))
                {
                    return StatusCode(StatusCodes.Status304NotModified);
                }
            }
            else if (!string.IsNullOrEmpty(Request.Headers.IfModifiedSince) &&
                     DateTimeOffset.TryParse(Request.Headers.IfModifiedSince.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var ifModifiedSince))
            {
                if (lastModifiedUtc <= ifModifiedSince.UtcDateTime || (lastModifiedUtc - ifModifiedSince.UtcDateTime).TotalSeconds < 1)
                {
                    return StatusCode(StatusCodes.Status304NotModified);
                }
            }
        }

        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    private string FindArtworkOnDisk(int torrentId, TorrentMediaMetadata meta, string type)
    {
        if (_appFolderInfo == null || string.IsNullOrWhiteSpace(_appFolderInfo.AppDataFolder))
        {
            return null;
        }

        var candidateDirs = new List<string>
        {
            Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover", torrentId.ToString()),
            Path.Combine(_appFolderInfo.AppDataFolder, "MediaCache", torrentId.ToString()),
        };

        if (meta != null)
        {
            if (!string.IsNullOrEmpty(meta.PosterLocalPath) &&
                !meta.PosterLocalPath.Contains("..") &&
                IsPathAllowed(meta.PosterLocalPath, out var fullPoster))
            {
                var dir = Path.GetDirectoryName(fullPoster);
                if (!string.IsNullOrEmpty(dir) && IsCandidateDirectoryAllowed(dir) && !candidateDirs.Contains(dir))
                {
                    candidateDirs.Add(dir);
                }
            }

            if (!string.IsNullOrEmpty(meta.BackdropLocalPath) &&
                !meta.BackdropLocalPath.Contains("..") &&
                IsPathAllowed(meta.BackdropLocalPath, out var fullBackdrop))
            {
                var dir = Path.GetDirectoryName(fullBackdrop);
                if (!string.IsNullOrEmpty(dir) && IsCandidateDirectoryAllowed(dir) && !candidateDirs.Contains(dir))
                {
                    candidateDirs.Add(dir);
                }
            }
        }

        var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".svg" };
        var candidateNames = new List<string> { type };

        if (string.Equals(type, "fanart", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(new[] { "backdrop", "background" });
        }
        else if (string.Equals(type, "backdrop", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(new[] { "fanart", "background" });
        }
        else if (string.Equals(type, "poster", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(new[] { "cover", "folder", "thumb" });
        }
        else if (string.Equals(type, "thumb", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(new[] { "poster", "cover", "folder" });
        }
        else if (string.Equals(type, "banner", StringComparison.OrdinalIgnoreCase))
        {
            candidateNames.AddRange(new[] { "season-banner", "fanart", "backdrop" });
        }

        var matchingFiles = new List<FileInfo>();

        foreach (var dir in candidateDirs)
        {
            if (!IsCandidateDirectoryAllowed(dir) || !Directory.Exists(dir))
            {
                continue;
            }

            foreach (var name in candidateNames)
            {
                foreach (var ext in extensions)
                {
                    var file = Path.Combine(dir, $"{name}{ext}");
                    if (global::System.IO.File.Exists(file) && IsPathAllowed(file, out _))
                    {
                        matchingFiles.Add(new FileInfo(file));
                    }
                }
            }
        }

        return matchingFiles
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    private bool IsPathAllowed(string path, out string fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (_appFolderInfo == null || string.IsNullOrWhiteSpace(_appFolderInfo.AppDataFolder))
        {
            _logger.Warn("AppDataFolder is not configured; refusing to serve media cover artwork from path: {0}", path);
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to resolve full path for artwork: {0}", path);
            return false;
        }

        string appDataRoot;
        string mediaCoverRoot;
        string mediaCacheRoot;
        try
        {
            appDataRoot = Path.GetFullPath(_appFolderInfo.AppDataFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            mediaCoverRoot = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            mediaCacheRoot = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCache"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to resolve allowed media cover directories from AppDataFolder: {0}", _appFolderInfo.AppDataFolder);
            return false;
        }

        var isWithinAllowedDir = (fullPath.StartsWith(mediaCoverRoot, StringComparison.OrdinalIgnoreCase) ||
                                  fullPath.StartsWith(mediaCacheRoot, StringComparison.OrdinalIgnoreCase)) &&
                                 fullPath.StartsWith(appDataRoot, StringComparison.OrdinalIgnoreCase);

        if (!isWithinAllowedDir)
        {
            _logger.Warn("Media cover artwork path '{0}' is outside allowed AppDataFolder directory.", fullPath);
            return false;
        }

        return true;
    }

    private bool IsCandidateDirectoryAllowed(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || dir.Contains("..") || _appFolderInfo == null || string.IsNullOrWhiteSpace(_appFolderInfo.AppDataFolder))
        {
            return false;
        }

        try
        {
            var fullDir = Path.GetFullPath(dir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var appDataRoot = Path.GetFullPath(_appFolderInfo.AppDataFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var mediaCoverRoot = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var mediaCacheRoot = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCache"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return (fullDir.StartsWith(mediaCoverRoot, StringComparison.OrdinalIgnoreCase) ||
                    fullDir.StartsWith(mediaCacheRoot, StringComparison.OrdinalIgnoreCase)) &&
                   fullDir.StartsWith(appDataRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to validate candidate directory path: {0}", dir);
            return false;
        }
    }
}
