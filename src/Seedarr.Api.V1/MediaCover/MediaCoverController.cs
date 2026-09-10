using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
        if (string.IsNullOrWhiteSpace(filename))
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

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    private ActionResult ServeArtwork(int torrentId, string type)
    {
        if (string.IsNullOrWhiteSpace(type) || !ValidArtworkTypes.Contains(type))
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

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };

        return PhysicalFile(Path.GetFullPath(path), contentType);
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Path is resolved internally from server metadata storage")]
    private string FindArtworkOnDisk(int torrentId, TorrentMediaMetadata meta, string type)
    {
        var candidateDirs = new List<string>();

        if (_appFolderInfo != null)
        {
            candidateDirs.Add(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCover", torrentId.ToString()));
            candidateDirs.Add(Path.Combine(_appFolderInfo.AppDataFolder, "MediaCache", torrentId.ToString()));
        }

        if (meta != null)
        {
            if (!string.IsNullOrEmpty(meta.PosterLocalPath))
            {
                var dir = Path.GetDirectoryName(meta.PosterLocalPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !candidateDirs.Contains(dir))
                {
                    candidateDirs.Add(dir);
                }
            }

            if (!string.IsNullOrEmpty(meta.BackdropLocalPath))
            {
                var dir = Path.GetDirectoryName(meta.BackdropLocalPath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !candidateDirs.Contains(dir))
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
                        return file;
                    }
                }
            }
        }

        return null;
    }
}
