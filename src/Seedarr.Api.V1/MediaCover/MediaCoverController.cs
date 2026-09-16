using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using Microsoft.AspNetCore.Authorization;
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
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        }

        return Content(svg, "image/svg+xml", Encoding.UTF8);
    }

    public static string GeneratePlaceholderSvg(string title, string category = null)
    {
        var safeTitle = !string.IsNullOrEmpty(title) ? SecurityElement.Escape(title) : string.Empty;
        var safeCategory = !string.IsNullOrEmpty(category) ? SecurityElement.Escape(category) : string.Empty;

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"300\" height=\"450\" viewBox=\"0 0 300 450\">" +
               $"<rect width=\"100%\" height=\"100%\" fill=\"#1e1e1e\"/>" +
               $"<text x=\"50%\" y=\"48%\" font-family=\"sans-serif\" font-size=\"18\" fill=\"#ffffff\" text-anchor=\"middle\" dominant-baseline=\"middle\">{safeTitle}</text>" +
               $"{(!string.IsNullOrEmpty(safeCategory) ? $"<text x=\"50%\" y=\"56%\" font-family=\"sans-serif\" font-size=\"14\" fill=\"#aaaaaa\" text-anchor=\"middle\">{safeCategory}</text>" : string.Empty)}" +
               $"</svg>";
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

        if (string.IsNullOrEmpty(path) || !global::System.IO.File.Exists(path))
        {
            path = FindArtworkOnDisk(torrentId, meta, type);
        }

        if (string.IsNullOrEmpty(path) || path.Contains("..") || !global::System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var fullPath = Path.GetFullPath(path);

        if (_appFolderInfo != null && !string.IsNullOrEmpty(_appFolderInfo.AppDataFolder))
        {
            var mediaCoverRoot = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var mediaCacheRoot = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCache"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(mediaCoverRoot, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(mediaCacheRoot, StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }
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

        if (Response != null)
        {
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
            }
        }

        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    private string FindArtworkOnDisk(int torrentId, TorrentMediaMetadata meta, string type)
    {
        var candidateDirs = new List<string>();

        if (_appFolderInfo != null && !string.IsNullOrEmpty(_appFolderInfo.AppDataFolder))
        {
            var mediaCoverRoot = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var mediaCacheRoot = Path.GetFullPath(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCache"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            candidateDirs.Add(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover", torrentId.ToString()));
            candidateDirs.Add(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCache", torrentId.ToString()));

            if (meta != null)
            {
                if (!string.IsNullOrEmpty(meta.PosterLocalPath))
                {
                    var fullPoster = Path.GetFullPath(meta.PosterLocalPath);
                    if (fullPoster.StartsWith(mediaCoverRoot, StringComparison.OrdinalIgnoreCase) ||
                        fullPoster.StartsWith(mediaCacheRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        var dir = Path.GetDirectoryName(fullPoster);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !candidateDirs.Contains(dir))
                        {
                            candidateDirs.Add(dir);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(meta.BackdropLocalPath))
                {
                    var fullBackdrop = Path.GetFullPath(meta.BackdropLocalPath);
                    if (fullBackdrop.StartsWith(mediaCoverRoot, StringComparison.OrdinalIgnoreCase) ||
                        fullBackdrop.StartsWith(mediaCacheRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        var dir = Path.GetDirectoryName(fullBackdrop);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !candidateDirs.Contains(dir))
                        {
                            candidateDirs.Add(dir);
                        }
                    }
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
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var name in candidateNames)
            {
                foreach (var ext in extensions)
                {
                    var file = Path.Combine(dir, $"{name}{ext}");
                    if (global::System.IO.File.Exists(file))
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
}
