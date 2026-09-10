using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Http.Security;

namespace Seedarr.Api.V1.QBittorrent;

[AllowAnonymous]
[ApiController]
[Route("api/v2")]
public class QBittorrentApiController : ControllerBase, IActionFilter
{
    private static readonly RpcSessionStore _authenticatedSessions = new();
    private static readonly ConcurrentDictionary<string, QBitSessionSyncState> _sessionSyncStates = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastSyncCleanupTime = DateTime.UtcNow;

    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ITorrentImportService _torrentImportService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IConfigService _configService;
    private readonly ITagService _tagService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public QBittorrentApiController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ITorrentImportService torrentImportService,
        ITrackerEntryService trackerEntryService,
        IConfigService configService,
        ITagService tagService = null,
        IConfigFileProvider configFileProvider = null,
        HttpClient httpClient = null)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _torrentImportService = torrentImportService;
        _trackerEntryService = trackerEntryService;
        _configService = configService;
        _tagService = tagService;
        _configFileProvider = configFileProvider;
        _httpClient = httpClient ?? new HttpClient();
        _logger = LogManager.GetCurrentClassLogger();
    }

    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        string actionName = null;
        if (context.ActionDescriptor?.RouteValues != null &&
            context.ActionDescriptor.RouteValues.TryGetValue("action", out var val))
        {
            actionName = val;
        }

        if (string.IsNullOrEmpty(actionName) &&
            context.ActionDescriptor is ControllerActionDescriptor cad)
        {
            actionName = cad.ActionName;
        }

        if (string.Equals(actionName, nameof(Login), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsAuthenticated())
        {
            context.Result = StatusCode(StatusCodes.Status403Forbidden, "Forbidden");
        }
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    private bool IsAuthenticated()
    {
        if (_configFileProvider == null || !_configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (User?.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        if (Request.Cookies.TryGetValue("SID", out var sid) && !string.IsNullOrWhiteSpace(sid))
        {
            if (_authenticatedSessions.IsValid(sid))
            {
                return true;
            }
        }

        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey) && Request.Query.TryGetValue("apikey", out var qKey))
        {
            apiKey = qKey.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(_configFileProvider.ApiKey))
        {
            if (RpcAuthenticationHelper.FixedTimeEquals(apiKey, _configFileProvider.ApiKey))
            {
                return true;
            }
        }

        return false;
    }

    [HttpPost("auth/login")]
    public ActionResult Login([FromForm] string username = null, [FromForm] string password = null)
    {
        if (_configFileProvider != null && _configFileProvider.AuthenticationEnabled)
        {
            var authenticated = false;
            var masterApiKey = _configFileProvider.ApiKey;

            if (!string.IsNullOrWhiteSpace(masterApiKey) &&
                ((!string.IsNullOrWhiteSpace(password) && RpcAuthenticationHelper.FixedTimeEquals(password, masterApiKey)) ||
                 (!string.IsNullOrWhiteSpace(username) && RpcAuthenticationHelper.FixedTimeEquals(username, masterApiKey))))
            {
                authenticated = true;
            }

            if (!authenticated)
            {
                return Content("Fails.", "text/plain");
            }
        }

        var sid = Guid.NewGuid().ToString("N");
        _authenticatedSessions.SetSession(sid, DateTime.UtcNow.AddDays(7));

        Response.Cookies.Append("SID", sid, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
        });

        return Content("Ok.", "text/plain");
    }

    [HttpPost("auth/logout")]
    public ActionResult Logout()
    {
        if (Request.Cookies.TryGetValue("SID", out var sid) && !string.IsNullOrWhiteSpace(sid))
        {
            _authenticatedSessions.RemoveSession(sid);
            _sessionSyncStates.TryRemove($"sid:{sid}", out _);
        }

        Response.Cookies.Delete("SID");
        return Content("Ok.", "text/plain");
    }

    [HttpGet("app/version")]
    public ActionResult<string> GetVersion()
    {
        return Content("v4.4.2", "text/plain");
    }

    [HttpGet("app/webapiVersion")]
    public ActionResult<string> GetWebApiVersion()
    {
        return Content("2.8.3", "text/plain");
    }

    [HttpGet("app/preferences")]
    public ActionResult<Dictionary<string, object>> GetPreferences()
    {
        var savePath = !string.IsNullOrWhiteSpace(_configService?.WatchFolderPath) ? _configService.WatchFolderPath : "/downloads";

        return Ok(new Dictionary<string, object>
        {
            ["save_path"] = savePath,
            ["temp_path_enabled"] = false,
            ["temp_path"] = Path.Combine(savePath, "incomplete"),
            ["listen_port"] = _configService?.ListeningPort ?? 6881,
            ["up_limit"] = (_configService?.MaxUploadSpeedKbps ?? 625) * 1024,
            ["dl_limit"] = (_configService?.MaxDownloadSpeedKbps ?? 1250) * 1024,
            ["max_connec"] = _configService?.MaxGlobalConnections ?? 200,
            ["max_connec_per_torrent"] = _configService?.MaxPerTorrentConnections ?? 50,
            ["dht"] = _configService?.EnableDht ?? true,
            ["pex"] = _configService?.EnablePex ?? true,
            ["lsd"] = _configService?.EnableLpd ?? true,
            ["encryption"] = 1,
            ["anonymous_mode"] = false,
            ["queueing_enabled"] = true,
            ["max_active_downloads"] = 5,
            ["max_active_uploads"] = 5,
            ["max_active_torrents"] = 10,
            ["dont_count_slow_torrents"] = false,
            ["slow_torrent_dl_rate_threshold"] = 2,
            ["slow_torrent_ul_rate_threshold"] = 2,
            ["incomplete_files_ext"] = false,
            ["alt_dl_limit"] = (_configService?.AltDownloadSpeedKbps ?? 100) * 1024,
            ["alt_up_limit"] = (_configService?.AltUploadSpeedKbps ?? 50) * 1024,
            ["enable_embedded_tracker"] = _configService?.TrackerServerEnabled ?? false,
            ["embedded_tracker_port"] = _configService?.TrackerHttpPort ?? 9696,
            ["auto_shutdown_on_downloads_finished"] = false,
        });
    }

    [HttpPost("app/setPreferences")]
    public async Task<ActionResult> SetPreferencesAsync([FromForm] string json = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            try
            {
                if (Request?.Body != null && Request.Body.CanRead)
                {
                    using var reader = new StreamReader(Request.Body);
                    json = await reader.ReadToEndAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to read body stream in SetPreferences");
            }
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return Content("Ok.", "text/plain");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var dict = new Dictionary<string, object>();

            if (root.TryGetProperty("save_path", out var sp) && sp.ValueKind == JsonValueKind.String)
            {
                dict["WatchFolderPath"] = sp.GetString();
            }

            if (root.TryGetProperty("listen_port", out var lp) && lp.TryGetInt32(out var port))
            {
                dict["ListeningPort"] = port;
            }

            if (root.TryGetProperty("dl_limit", out var dl) && dl.TryGetInt32(out var dlBytes))
            {
                dict["MaxDownloadSpeedKbps"] = dlBytes / 1024;
            }

            if (root.TryGetProperty("up_limit", out var ul) && ul.TryGetInt32(out var ulBytes))
            {
                dict["MaxUploadSpeedKbps"] = ulBytes / 1024;
            }

            if (root.TryGetProperty("alt_dl_limit", out var adl) && adl.TryGetInt32(out var adlBytes))
            {
                dict["AltDownloadSpeedKbps"] = adlBytes / 1024;
            }

            if (root.TryGetProperty("alt_up_limit", out var aul) && aul.TryGetInt32(out var aulBytes))
            {
                dict["AltUploadSpeedKbps"] = aulBytes / 1024;
            }

            if (root.TryGetProperty("max_connec", out var mc) && mc.TryGetInt32(out var maxConnec))
            {
                dict["MaxGlobalConnections"] = maxConnec;
            }

            if (root.TryGetProperty("max_connec_per_torrent", out var mcpt) && mcpt.TryGetInt32(out var maxPerTor))
            {
                dict["MaxPerTorrentConnections"] = maxPerTor;
            }

            if (root.TryGetProperty("dht", out var dht) && (dht.ValueKind == JsonValueKind.True || dht.ValueKind == JsonValueKind.False))
            {
                dict["EnableDht"] = dht.GetBoolean();
            }

            if (root.TryGetProperty("pex", out var pex) && (pex.ValueKind == JsonValueKind.True || pex.ValueKind == JsonValueKind.False))
            {
                dict["EnablePex"] = pex.GetBoolean();
            }

            if (root.TryGetProperty("lsd", out var lsd) && (lsd.ValueKind == JsonValueKind.True || lsd.ValueKind == JsonValueKind.False))
            {
                dict["EnableLpd"] = lsd.GetBoolean();
            }

            if (dict.Count > 0 && _configService != null)
            {
                _configService.SaveConfigDictionary(dict);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse qBittorrent preferences payload");
        }

        return Content("Ok.", "text/plain");
    }

    [HttpGet("app/defaultSavePath")]
    public ActionResult<string> GetDefaultSavePath()
    {
        return Content(_configService?.WatchFolderPath ?? "/downloads", "text/plain");
    }

    [HttpGet("torrents/info")]
    public ActionResult<List<Dictionary<string, object>>> GetTorrentsInfo(
        [FromQuery] string filter = null,
        [FromQuery] string category = null,
        [FromQuery] string tag = null,
        [FromQuery] string hashes = null)
    {
        var torrents = _torrentService.GetAll();

        if (!string.IsNullOrEmpty(hashes) && !string.Equals(hashes.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(h => h.Trim().ToLowerInvariant())
                .ToHashSet();
            torrents = torrents.Where(t => t.InfoHash != null && hashList.Contains(t.InfoHash.ToLowerInvariant())).ToList();
        }

        if (!string.IsNullOrEmpty(category))
        {
            torrents = torrents.Where(t => string.Equals(t.Label, category, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var filterTags = tag.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (filterTags.Count > 0)
            {
                torrents = torrents.Where(t =>
                {
                    if (string.IsNullOrEmpty(t.Label))
                    {
                        return false;
                    }

                    var torrentTags = t.Label.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrEmpty(l));

                    return filterTags.Any(ft => torrentTags.Any(tt => string.Equals(tt, ft, StringComparison.OrdinalIgnoreCase)));
                }).ToList();
            }
        }

        if (!string.IsNullOrEmpty(filter))
        {
            switch (filter.ToLowerInvariant())
            {
                case "downloading":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Downloading).ToList();
                    break;
                case "seeding":
                case "completed":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Seeding || t.Progress >= 1.0).ToList();
                    break;
                case "paused":
                case "stopped":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped).ToList();
                    break;
                case "active":
                    torrents = torrents.Where(t => t.DownloadSpeed > 0 || t.UploadSpeed > 0).ToList();
                    break;
                case "inactive":
                    torrents = torrents.Where(t => t.DownloadSpeed == 0 && t.UploadSpeed == 0).ToList();
                    break;
                case "stalled":
                    torrents = torrents.Where(t => (t.Status == TorrentStatus.Downloading && t.DownloadSpeed == 0) || (t.Status == TorrentStatus.Seeding && t.UploadSpeed == 0)).ToList();
                    break;
                case "stalled_downloading":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Downloading && t.DownloadSpeed == 0).ToList();
                    break;
                case "stalled_uploading":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Seeding && t.UploadSpeed == 0).ToList();
                    break;
                case "errored":
                case "error":
                    torrents = torrents.Where(t => t.Status == TorrentStatus.Error).ToList();
                    break;
                case "resumed":
                case "running":
                    torrents = torrents.Where(t => t.Status != TorrentStatus.Paused && t.Status != TorrentStatus.Stopped).ToList();
                    break;
            }
        }

        var result = torrents.Select(t =>
        {
            var state = MapToQBitState(t.Status, t.Progress);
            var savePath = !string.IsNullOrWhiteSpace(t.SourcePath) ? t.SourcePath : (_configService?.WatchFolderPath ?? "/downloads");
            var contentPath = Path.Combine(savePath, t.Name ?? string.Empty);
            var amountLeft = (long)(t.TotalSize * Math.Max(0.0, 1.0 - t.Progress));

            return new Dictionary<string, object>
            {
                ["hash"] = t.InfoHash ?? string.Empty,
                ["name"] = t.Name ?? string.Empty,
                ["size"] = t.TotalSize,
                ["total_size"] = t.TotalSize,
                ["progress"] = t.Progress,
                ["dlspeed"] = t.DownloadSpeed,
                ["upspeed"] = t.UploadSpeed,
                ["priority"] = t.Priority,
                ["num_seeds"] = t.Seeders,
                ["num_leechs"] = t.Leechers,
                ["num_complete"] = t.Seeders,
                ["num_incomplete"] = t.Leechers,
                ["ratio"] = t.Ratio,
                ["eta"] = CalculateEta(t),
                ["state"] = state,
                ["seq_dl"] = t.SequentialDownload,
                ["f_l_piece_prio"] = false,
                ["category"] = t.Label ?? string.Empty,
                ["tags"] = t.Label ?? string.Empty,
                ["save_path"] = savePath,
                ["content_path"] = contentPath,
                ["added_on"] = new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds(),
                ["completion_on"] = t.Progress >= 1.0 ? new DateTimeOffset(t.LastActive ?? t.DateAdded).ToUnixTimeSeconds() : -1,
                ["amount_left"] = amountLeft,
                ["downloaded"] = t.Downloaded,
                ["uploaded"] = t.Uploaded,
                ["max_ratio"] = -1.0,
                ["max_seeding_time"] = -1,
                ["ratio_limit"] = -2.0,
                ["seeding_time_limit"] = -2,
                ["seeding_time"] = t.SeedingTime,
                ["last_activity"] = new DateTimeOffset(t.LastActive ?? t.DateAdded).ToUnixTimeSeconds(),
                ["is_private"] = t.IsPrivate,
                ["private"] = t.IsPrivate,
                ["super_seeding"] = t.SuperSeeding,
            };
        }).ToList();

        return Ok(result);
    }

    [HttpPost("torrents/add")]
    public async Task<ActionResult> AddTorrents([FromForm] QBitAddTorrentsRequest request = null)
    {
        request ??= new QBitAddTorrentsRequest();

        await AddTorrentsFromUrlsAsync(request);
        await AddTorrentsFromFilesAsync(request);

        return Content("Ok.", "text/plain");
    }

    private async Task AddTorrentsFromUrlsAsync(QBitAddTorrentsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Urls))
        {
            return;
        }

        var lines = request.Urls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var url in lines)
        {
            var trimmed = url.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var added = _torrentImportService.ImportFromMagnet(trimmed);
                    ApplyTorrentRequestOptions(added, request);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to import torrent from magnet: {0}", trimmed);
                }

                continue;
            }

            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                await AddTorrentFromHttpUrlAsync(trimmed, request);
            }
        }
    }

    private async Task AddTorrentFromHttpUrlAsync(string url, QBitAddTorrentsRequest request)
    {
        try
        {
            using var reqMsg = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(request.EffectiveCookie))
            {
                reqMsg.Headers.TryAddWithoutValidation("Cookie", request.EffectiveCookie);
            }

            using var resp = await _httpClient.SendAsync(reqMsg);
            resp.EnsureSuccessStatusCode();

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            using var ms = new MemoryStream(bytes);
            var added = _torrentImportService.ImportFromFile(ms, "downloaded.torrent");
            ApplyTorrentRequestOptions(added, request);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to download torrent file from URL: {0}", url);
        }
    }

    private async Task AddTorrentsFromFilesAsync(QBitAddTorrentsRequest request)
    {
        if (request?.Torrents == null || request.Torrents.Count == 0)
        {
            return;
        }

        foreach (var file in request.Torrents)
        {
            if (file == null || file.Length <= 0)
            {
                continue;
            }

            try
            {
                using var stream = file.OpenReadStream();
                var added = _torrentImportService.ImportFromFile(stream, file.FileName);
                ApplyTorrentRequestOptions(added, request);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to import uploaded torrent file: {0}", file.FileName);
            }
        }

        await Task.CompletedTask;
    }

    private void ApplyTorrentRequestOptions(Torrent added, QBitAddTorrentsRequest request)
    {
        if (added == null || request == null)
        {
            return;
        }

        var needsUpdate = false;
        var tagOrCategory = !string.IsNullOrWhiteSpace(request.Category) ? request.Category : request.Tags;
        if (!string.IsNullOrWhiteSpace(tagOrCategory))
        {
            added.Label = tagOrCategory;
            needsUpdate = true;
        }

        if (!string.IsNullOrWhiteSpace(request.EffectiveSavePath))
        {
            added.SourcePath = request.EffectiveSavePath;
            needsUpdate = true;
        }

        if (request.IsSequential)
        {
            added.SequentialDownload = true;
            needsUpdate = true;
        }

        if (request.IsPaused)
        {
            added.Pause();
            needsUpdate = true;
        }

        if (needsUpdate)
        {
            _torrentService.Update(added);
        }
    }

    [HttpPost("torrents/pause")]
    [HttpPost("torrents/stop")]
    public ActionResult PauseTorrents([FromForm] string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return BadRequest();
        }

        foreach (var torrent in ResolveTorrents(hashes))
        {
            torrent.Pause();
            _torrentService.Update(torrent);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/resume")]
    [HttpPost("torrents/start")]
    public ActionResult ResumeTorrents([FromForm] string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return BadRequest();
        }

        foreach (var torrent in ResolveTorrents(hashes))
        {
            torrent.Resume();
            _torrentService.Update(torrent);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/delete")]
    public ActionResult DeleteTorrents(
        [FromForm] string hashes,
        [FromForm] bool deleteFiles = false)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return BadRequest();
        }

        foreach (var torrent in ResolveTorrents(hashes))
        {
            _torrentService.Delete(torrent.Id, deleteFiles);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpGet("torrents/files")]
    public ActionResult<List<Dictionary<string, object>>> GetFiles([FromQuery] string hash)
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        if (torrent == null)
        {
            return NotFound();
        }

        var files = _torrentFileService.GetByTorrentId(torrent.Id);
        var result = files.Select((f, index) => new Dictionary<string, object>
        {
            ["index"] = index,
            ["name"] = f.Path ?? string.Empty,
            ["size"] = f.Size,
            ["progress"] = torrent.Progress,
            ["priority"] = 1,
            ["is_seed"] = torrent.Progress >= 1.0,
            ["piece_range"] = new[] { f.PieceOffset, f.PieceOffset + f.PieceCount - 1 },
        }).ToList();

        return Ok(result);
    }

    [HttpGet("torrents/trackers")]
    public ActionResult<List<Dictionary<string, object>>> GetTrackers([FromQuery] string hash)
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        if (torrent == null)
        {
            return NotFound();
        }

        var dbTrackers = _trackerEntryService.GetByTorrentId(torrent.Id);
        var trackers = new List<Dictionary<string, object>>();

        if (dbTrackers.Count > 0)
        {
            foreach (var t in dbTrackers)
            {
                var qbStatus = !t.Enabled ? 0 : (t.Status == TrackerStatus.Failed ? 4 : (t.Status == TrackerStatus.Unknown ? 1 : 2));

                trackers.Add(new Dictionary<string, object>
                {
                    ["url"] = t.Url ?? string.Empty,
                    ["status"] = qbStatus,
                    ["num_peers"] = t.Seeders + t.Leechers,
                    ["num_seeds"] = t.Seeders,
                    ["num_leeches"] = t.Leechers,
                    ["num_downloaded"] = t.Downloaded,
                    ["msg"] = t.ErrorMessage ?? string.Empty,
                });
            }
        }
        else
        {
            trackers.Add(new Dictionary<string, object>
            {
                ["url"] = torrent.TrackerUrl ?? string.Empty,
                ["status"] = 2,
                ["num_peers"] = torrent.Seeders + torrent.Leechers,
                ["num_seeds"] = torrent.Seeders,
                ["num_leeches"] = torrent.Leechers,
                ["num_downloaded"] = 0,
                ["msg"] = string.Empty,
            });
        }

        return Ok(trackers);
    }

    [HttpPost("torrents/addTrackers")]
    public ActionResult AddTrackers([FromForm] string hash, [FromForm] string urls)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(urls))
        {
            var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
            if (torrent != null)
            {
                var existingTrackers = _trackerEntryService.GetByTorrentId(torrent.Id);
                var existingUrls = existingTrackers
                    .Where(t => !string.IsNullOrWhiteSpace(t.Url))
                    .Select(t => t.Url.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var urlList = urls.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var url in urlList)
                {
                    var trimmed = url.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && !existingUrls.Contains(trimmed))
                    {
                        existingUrls.Add(trimmed);
                        _trackerEntryService.Add(new TrackerEntry
                        {
                            TorrentId = torrent.Id,
                            Url = trimmed,
                            Tier = 0,
                            Enabled = true,
                            Status = TrackerStatus.Working,
                            AnnounceInterval = 1800,
                            MinAnnounceInterval = 300,
                        });
                    }
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeTrackers")]
    public ActionResult RemoveTrackers([FromForm] string hash, [FromForm] string urls)
    {
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(urls))
        {
            var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
            if (torrent != null)
            {
                var urlSet = urls.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(u => u.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var existing = _trackerEntryService.GetByTorrentId(torrent.Id);
                foreach (var t in existing.Where(t => urlSet.Contains(t.Url)))
                {
                    _trackerEntryService.Delete(t.Id);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/editTracker")]
    public ActionResult EditTracker(
        [FromForm] string hash,
        [FromForm] string origUrl,
        [FromForm] string newUrl)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(origUrl) || string.IsNullOrWhiteSpace(newUrl))
        {
            return BadRequest();
        }

        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        if (torrent == null)
        {
            return NotFound();
        }

        var trimmedOrig = origUrl.Trim();
        var trimmedNew = newUrl.Trim();

        var existing = _trackerEntryService.GetByTorrentId(torrent.Id);
        var match = existing.FirstOrDefault(t => string.Equals(t.Url, trimmedOrig, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            match.Url = trimmedNew;
            _trackerEntryService.Update(match);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/recheck")]
    public ActionResult RecheckTorrents([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var torrent in ResolveTorrents(hashes))
            {
                _torrentService.Recheck(torrent.Id);
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/reannounce")]
    public ActionResult ReannounceTorrents([FromForm] string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return BadRequest();
        }

        foreach (var torrent in ResolveTorrents(hashes))
        {
            torrent.LastActive = DateTime.UtcNow;
            _torrentService.Update(torrent);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/rename")]
    public ActionResult RenameTorrent([FromForm] string hash, [FromForm] string name)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(name))
        {
            return BadRequest();
        }

        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        if (torrent == null)
        {
            return NotFound();
        }

        torrent.Name = name.Trim();
        _torrentService.Update(torrent);
        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setSuperSeeding")]
    public ActionResult SetSuperSeeding([FromForm] string hashes, [FromForm] bool value)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return BadRequest();
        }

        foreach (var torrent in ResolveTorrents(hashes))
        {
            torrent.SuperSeeding = value;
            _torrentService.Update(torrent);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setForceStart")]
    public ActionResult SetForceStart(
        [FromForm] string hashes,
        [FromForm] string value = null,
        [FromForm] bool? enable = null)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return BadRequest();
        }

        var force = enable ?? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        foreach (var torrent in ResolveTorrents(hashes))
        {
            torrent.ForceStart = force;
            if (force)
            {
                torrent.Resume();
            }

            _torrentService.Update(torrent);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setCategory")]
    public ActionResult SetCategory([FromForm] string hashes, [FromForm] string category)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return Content("Ok.", "text/plain");
        }

        foreach (var torrent in ResolveTorrents(hashes))
        {
            torrent.Label = category ?? string.Empty;
            _torrentService.Update(torrent);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpGet("torrents/categories")]
    public ActionResult<Dictionary<string, object>> GetCategories()
    {
        var torrents = _torrentService.GetAll();
        var defaultPath = _configService?.WatchFolderPath ?? "/downloads";
        var distinctLabels = torrents
            .Select(t => t.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = distinctLabels.ToDictionary(
            c => c,
            c => (object)new { name = c, savePath = Path.Combine(defaultPath, c) });

        return Ok(result);
    }

    [HttpPost("torrents/createCategory")]
    public ActionResult CreateCategory([FromForm] string category, [FromForm] string savePath)
    {
        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/editCategory")]
    public ActionResult EditCategory([FromForm] string category, [FromForm] string savePath)
    {
        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeCategories")]
    public ActionResult RemoveCategories([FromForm] string categories)
    {
        return Content("Ok.", "text/plain");
    }

    [HttpGet("torrents/tags")]
    public ActionResult<List<string>> GetTags()
    {
        var tags = _torrentService.GetAll()
            .Select(t => t.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(tags);
    }

    [HttpPost("torrents/createTags")]
    public ActionResult CreateTags([FromForm] string tags)
    {
        if (!string.IsNullOrWhiteSpace(tags) && _tagService != null)
        {
            var tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var existingTags = _tagService.GetAll().Select(x => x.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tagList)
            {
                var trimmed = t.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !existingTags.Contains(trimmed))
                {
                    _tagService.Add(new Tag { Label = trimmed });
                    existingTags.Add(trimmed);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/addTags")]
    public ActionResult AddTags([FromForm] string hashes, [FromForm] string tags)
    {
        if (string.IsNullOrWhiteSpace(hashes) || string.IsNullOrEmpty(tags))
        {
            return Content("Ok.", "text/plain");
        }

        var newTags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (newTags.Count == 0)
        {
            return Content("Ok.", "text/plain");
        }

        foreach (var torrent in ResolveTorrents(hashes))
        {
            var currentTags = (torrent.Label ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            foreach (var tag in newTags)
            {
                if (!currentTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    currentTags.Add(tag);
                }
            }

            torrent.Label = string.Join(", ", currentTags);
            _torrentService.Update(torrent);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/removeTags")]
    public ActionResult RemoveTags([FromForm] string hashes, [FromForm] string tags)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            var tagsToRemove = (tags ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var torrent in ResolveTorrents(hashes))
            {
                if (!string.IsNullOrEmpty(torrent.Label))
                {
                    if (tagsToRemove.Count == 0)
                    {
                        torrent.Label = string.Empty;
                    }
                    else
                    {
                        var remaining = torrent.Label.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim())
                            .Where(t => !tagsToRemove.Contains(t));
                        torrent.Label = string.Join(", ", remaining);
                    }

                    _torrentService.Update(torrent);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/deleteTags")]
    public ActionResult DeleteTags([FromForm] string tags)
    {
        if (!string.IsNullOrEmpty(tags))
        {
            var tagsToDelete = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var all = _torrentService.GetAll();
            foreach (var torrent in all)
            {
                if (!string.IsNullOrEmpty(torrent.Label))
                {
                    var remaining = torrent.Label.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim())
                        .Where(t => !tagsToDelete.Contains(t));
                    torrent.Label = string.Join(", ", remaining);
                    _torrentService.Update(torrent);
                }
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpGet("torrents/properties")]
    public ActionResult<Dictionary<string, object>> GetProperties([FromQuery] string hash)
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        if (torrent == null)
        {
            return NotFound();
        }

        var addedDate = new DateTimeOffset(torrent.DateAdded).ToUnixTimeSeconds();
        var savePath = !string.IsNullOrWhiteSpace(torrent.SourcePath) ? torrent.SourcePath : (_configService?.WatchFolderPath ?? "/downloads");

        return Ok(new Dictionary<string, object>
        {
            ["save_path"] = savePath,
            ["creation_date"] = addedDate,
            ["addition_date"] = addedDate,
            ["completion_date"] = torrent.Progress >= 1.0 ? new DateTimeOffset(torrent.LastActive ?? torrent.DateAdded).ToUnixTimeSeconds() : 0L,
            ["created_by"] = torrent.CreatedBy ?? string.Empty,
            ["dl_speed"] = torrent.DownloadSpeed,
            ["dl_speed_avg"] = torrent.DownloadSpeed,
            ["up_speed"] = torrent.UploadSpeed,
            ["up_speed_avg"] = torrent.UploadSpeed,
            ["eta"] = torrent.Eta,
            ["peers"] = torrent.Leechers,
            ["peers_total"] = torrent.Leechers,
            ["seeds"] = torrent.Seeders,
            ["seeds_total"] = torrent.Seeders,
            ["total_size"] = torrent.TotalSize,
            ["total_wasted"] = 0L,
            ["piece_size"] = torrent.PieceLength,
            ["pieces_num"] = torrent.PieceCount,
            ["pieces_have"] = (int)(torrent.PieceCount * torrent.Progress),
            ["total_downloaded"] = torrent.Downloaded,
            ["total_uploaded"] = torrent.Uploaded,
            ["up_limit"] = torrent.UploadLimit,
            ["dl_limit"] = torrent.DownloadLimit,
            ["time_elapsed"] = (int)(DateTime.UtcNow - torrent.DateAdded).TotalSeconds,
            ["seeding_time"] = (int)torrent.SeedingTime,
            ["nb_connections"] = torrent.Seeders + torrent.Leechers,
            ["share_ratio"] = torrent.Ratio,
            ["is_private"] = torrent.IsPrivate,
            ["private"] = torrent.IsPrivate,
            ["super_seeding"] = torrent.SuperSeeding,
            ["comment"] = torrent.Comment ?? string.Empty,
        });
    }

    [HttpGet("torrents/pieceStates")]
    public ActionResult<List<int>> GetPieceStates([FromQuery] string hash)
    {
        var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        if (torrent == null)
        {
            return NotFound();
        }

        var pieceCount = torrent.PieceCount > 0 ? torrent.PieceCount : 100;
        var states = new List<int>(pieceCount);
        var completedCount = (int)(pieceCount * torrent.Progress);
        for (var i = 0; i < pieceCount; i++)
        {
            states.Add(i < completedCount ? 2 : 0);
        }

        return Ok(states);
    }

    [HttpGet("sync/maindata")]
    public ActionResult<Dictionary<string, object>> GetMainData([FromQuery] int rid = 0)
    {
        CleanupExpiredSessionSyncStates();

        var sessionKey = GetClientSessionKey();
        var sessionState = _sessionSyncStates.GetOrAdd(sessionKey, _ => new QBitSessionSyncState());

        lock (sessionState.Lock)
        {
            sessionState.LastAccessed = DateTime.UtcNow;

            var torrents = _torrentService.GetAll();
            var defaultPath = _configService?.WatchFolderPath ?? "/downloads";

            var categories = torrents
                .Select(t => t.Label)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    c => c,
                    c => (object)new { name = c, savePath = Path.Combine(defaultPath, c) });

            var dlLimit = ((_configService?.AlternativeSpeedEnabled == true ? _configService?.AltDownloadSpeedKbps : _configService?.MaxDownloadSpeedKbps) ?? 1250) * 1024;
            var upLimit = ((_configService?.AlternativeSpeedEnabled == true ? _configService?.AltUploadSpeedKbps : _configService?.MaxUploadSpeedKbps) ?? 625) * 1024;
            var totalDl = torrents.Sum(t => t.Downloaded);
            var totalUl = torrents.Sum(t => t.Uploaded);
            var globalRatio = totalDl > 0 ? (double)totalUl / totalDl : 0.0;

            var serverState = new
            {
                dl_info_speed = torrents.Sum(t => t.DownloadSpeed),
                up_info_speed = torrents.Sum(t => t.UploadSpeed),
                dl_info_data = totalDl,
                up_info_data = totalUl,
                alltime_dl = totalDl,
                alltime_ul = totalUl,
                dl_rate_limit = dlLimit,
                up_rate_limit = upLimit,
                use_alt_speed_limits = _configService?.AlternativeSpeedEnabled ?? false,
                use_alt_dl_limit = _configService?.AlternativeSpeedEnabled ?? false,
                use_alt_up_limit = _configService?.AlternativeSpeedEnabled ?? false,
                alt_dl_limit = (_configService?.AltDownloadSpeedKbps ?? 100) * 1024,
                alt_up_limit = (_configService?.AltUploadSpeedKbps ?? 50) * 1024,
                connection_status = "connected",
                dht_nodes = 0,
                free_space_on_disk = 100L * 1024 * 1024 * 1024,
                global_ratio = Math.Round(globalRatio, 2),
                refresh_interval = 2000,
            };

            var currentHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (rid <= 0 || !sessionState.Initialized || rid > sessionState.CurrentRid)
            {
                sessionState.Initialized = true;
                sessionState.CurrentRid = rid <= 0 ? 1 : rid + 1;
                sessionState.CachedTorrents.Clear();
                sessionState.RemovedTorrents.Clear();

                var torrentDict = new Dictionary<string, object>();
                foreach (var t in torrents)
                {
                    currentHashes.Add(t.InfoHash);
                    var savePath = !string.IsNullOrWhiteSpace(t.SourcePath) ? t.SourcePath : defaultPath;
                    var contentPath = Path.Combine(savePath, t.Name ?? string.Empty);
                    var snapshot = QBitTorrentSnapshot.FromTorrent(t, savePath, contentPath);

                    sessionState.CachedTorrents[t.InfoHash] = (snapshot, sessionState.CurrentRid);

                    torrentDict[t.InfoHash] = new
                    {
                        name = snapshot.Name,
                        size = snapshot.Size,
                        total_size = snapshot.Size,
                        progress = snapshot.Progress,
                        dlspeed = snapshot.DlSpeed,
                        upspeed = snapshot.UpSpeed,
                        state = snapshot.State,
                        category = snapshot.Category,
                        tags = snapshot.Tags,
                        save_path = snapshot.SavePath,
                        content_path = snapshot.ContentPath,
                        eta = snapshot.Eta,
                        ratio = snapshot.Ratio,
                        num_seeds = snapshot.NumSeeds,
                        num_complete = snapshot.NumSeeds,
                        num_leechs = snapshot.NumLeechs,
                        num_incomplete = snapshot.NumLeechs,
                        downloaded = snapshot.Downloaded,
                        uploaded = snapshot.Uploaded,
                        amount_left = snapshot.AmountLeft,
                        added_on = snapshot.AddedOn,
                        completion_on = snapshot.CompletionOn,
                        seq_dl = snapshot.SeqDl,
                        f_l_piece_prio = snapshot.FLPiecePrio,
                    };
                }

                return Ok(new Dictionary<string, object>
                {
                    ["rid"] = sessionState.CurrentRid,
                    ["full_update"] = true,
                    ["torrents"] = torrentDict,
                    ["categories"] = categories,
                    ["server_state"] = serverState,
                });
            }

            var nextRid = sessionState.CurrentRid + 1;
            var updatedTorrents = new Dictionary<string, object>();

            foreach (var t in torrents)
            {
                currentHashes.Add(t.InfoHash);
                var savePath = !string.IsNullOrWhiteSpace(t.SourcePath) ? t.SourcePath : defaultPath;
                var contentPath = Path.Combine(savePath, t.Name ?? string.Empty);
                var snapshot = QBitTorrentSnapshot.FromTorrent(t, savePath, contentPath);

                var isNewOrChanged = !sessionState.CachedTorrents.TryGetValue(t.InfoHash, out var existing) || existing.Snapshot != snapshot;
                if (isNewOrChanged)
                {
                    sessionState.CachedTorrents[t.InfoHash] = (snapshot, nextRid);
                }

                if (isNewOrChanged || existing.ChangedAtRid > rid)
                {
                    updatedTorrents[t.InfoHash] = new
                    {
                        name = snapshot.Name,
                        size = snapshot.Size,
                        total_size = snapshot.Size,
                        progress = snapshot.Progress,
                        dlspeed = snapshot.DlSpeed,
                        upspeed = snapshot.UpSpeed,
                        state = snapshot.State,
                        category = snapshot.Category,
                        tags = snapshot.Tags,
                        save_path = snapshot.SavePath,
                        content_path = snapshot.ContentPath,
                        eta = snapshot.Eta,
                        ratio = snapshot.Ratio,
                        num_seeds = snapshot.NumSeeds,
                        num_complete = snapshot.NumSeeds,
                        num_leechs = snapshot.NumLeechs,
                        num_incomplete = snapshot.NumLeechs,
                        downloaded = snapshot.Downloaded,
                        uploaded = snapshot.Uploaded,
                        amount_left = snapshot.AmountLeft,
                        added_on = snapshot.AddedOn,
                        completion_on = snapshot.CompletionOn,
                        seq_dl = snapshot.SeqDl,
                        f_l_piece_prio = snapshot.FLPiecePrio,
                    };
                }
            }

            var removedNow = sessionState.CachedTorrents.Keys.Where(h => !currentHashes.Contains(h)).ToList();
            foreach (var hash in removedNow)
            {
                sessionState.CachedTorrents.Remove(hash);
                sessionState.RemovedTorrents.Add((hash, nextRid));
            }

            if (sessionState.RemovedTorrents.Count > 500)
            {
                sessionState.RemovedTorrents.RemoveRange(0, sessionState.RemovedTorrents.Count - 500);
            }

            var torrentsRemoved = sessionState.RemovedTorrents
                .Where(r => r.RemovedAtRid > rid)
                .Select(r => r.Hash)
                .ToList();

            sessionState.CurrentRid = nextRid;

            return Ok(new Dictionary<string, object>
            {
                ["rid"] = sessionState.CurrentRid,
                ["full_update"] = false,
                ["torrents"] = updatedTorrents,
                ["torrents_removed"] = torrentsRemoved,
                ["categories"] = categories,
                ["server_state"] = serverState,
            });
        }
    }

    [HttpGet("sync/torrentPeers")]
    public ActionResult GetTorrentPeers([FromQuery] string hash, [FromQuery] int rid = 0)
    {
        return Ok(new
        {
            full_update = true,
            peers = new Dictionary<string, object>(),
            rid = rid <= 0 ? 1 : rid + 1,
            show_flags = true,
        });
    }

    [HttpGet("transfer/info")]
    public ActionResult<Dictionary<string, object>> GetTransferInfo()
    {
        var torrents = _torrentService.GetAll();
        return Ok(new Dictionary<string, object>
        {
            ["dl_info_speed"] = torrents.Sum(t => t.DownloadSpeed),
            ["up_info_speed"] = torrents.Sum(t => t.UploadSpeed),
            ["dl_info_data"] = torrents.Sum(t => t.Downloaded),
            ["up_info_data"] = torrents.Sum(t => t.Uploaded),
            ["connection_status"] = "connected",
        });
    }

    [HttpGet("transfer/speedLimitsMode")]
    public ActionResult<int> GetSpeedLimitsMode()
    {
        return Ok((_configService?.AlternativeSpeedEnabled == true) ? 1 : 0);
    }

    [HttpPost("transfer/toggleSpeedLimitsMode")]
    public ActionResult ToggleSpeedLimitsMode()
    {
        if (_configService != null)
        {
            var newState = !_configService.AlternativeSpeedEnabled;
            _configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                ["AlternativeSpeedEnabled"] = newState,
            });
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("transfer/setSpeedLimitsMode")]
    public ActionResult SetSpeedLimitsMode([FromForm] int mode)
    {
        if (_configService != null)
        {
            var enabled = mode == 1;
            _configService.SaveConfigDictionary(new Dictionary<string, object>
            {
                ["AlternativeSpeedEnabled"] = enabled,
            });
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("transfer/setDownloadLimit")]
    public ActionResult SetTransferDownloadLimit([FromForm] long limit)
    {
        _configService?.SaveConfigDictionary(new Dictionary<string, object> { ["MaxDownloadSpeedKbps"] = (int)(limit / 1024) });
        return Content("Ok.", "text/plain");
    }

    [HttpPost("transfer/setUploadLimit")]
    public ActionResult SetTransferUploadLimit([FromForm] long limit)
    {
        _configService?.SaveConfigDictionary(new Dictionary<string, object> { ["MaxUploadSpeedKbps"] = (int)(limit / 1024) });
        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setDownloadLimit")]
    public ActionResult SetTorrentDownloadLimit([FromForm] string hashes, [FromForm] long limit)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var t in ResolveTorrents(hashes))
            {
                t.DownloadLimit = (int)(limit / 1024);
                _torrentService.Update(t);
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setUploadLimit")]
    public ActionResult SetTorrentUploadLimit([FromForm] string hashes, [FromForm] long limit)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            foreach (var t in ResolveTorrents(hashes))
            {
                t.UploadLimit = (int)(limit / 1024);
                _torrentService.Update(t);
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setLocation")]
    [HttpPost("torrents/setSavePath")]
    public ActionResult SetLocation([FromForm] string hashes, [FromForm] string location)
    {
        if (!string.IsNullOrWhiteSpace(hashes) && !string.IsNullOrWhiteSpace(location))
        {
            foreach (var t in ResolveTorrents(hashes))
            {
                t.SourcePath = location;
                _torrentService.Update(t);
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/topPrio")]
    public ActionResult TopPrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            var torrents = ResolveTorrents(hashes);
            for (var i = torrents.Count - 1; i >= 0; i--)
            {
                _torrentService.MoveQueue(torrents[i].Id, "top");
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/bottomPrio")]
    public ActionResult BottomPrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            var torrents = ResolveTorrents(hashes);
            foreach (var t in torrents)
            {
                _torrentService.MoveQueue(t.Id, "bottom");
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/increasePrio")]
    public ActionResult IncreasePrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            var torrents = ResolveTorrents(hashes).OrderBy(t => t.SortOrder).ToList();
            foreach (var t in torrents)
            {
                _torrentService.MoveQueue(t.Id, "up");
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/decreasePrio")]
    public ActionResult DecreasePrio([FromForm] string hashes)
    {
        if (!string.IsNullOrWhiteSpace(hashes))
        {
            var torrents = ResolveTorrents(hashes).OrderByDescending(t => t.SortOrder).ToList();
            foreach (var t in torrents)
            {
                _torrentService.MoveQueue(t.Id, "down");
            }
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/toggleSequentialDownload")]
    public ActionResult ToggleSequentialDownload([FromForm] string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return BadRequest();
        }

        foreach (var torrent in ResolveTorrents(hashes))
        {
            torrent.SequentialDownload = !torrent.SequentialDownload;
            _torrentService.Update(torrent);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/setSequentialDownload")]
    public ActionResult SetSequentialDownload(
        [FromForm] string hashes,
        [FromForm] string value = null,
        [FromForm] bool? enable = null,
        [FromForm] bool? sequential = null)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return BadRequest();
        }

        var enabled = enable ?? sequential ?? (bool.TryParse(value, out var b) ? b : true);

        foreach (var torrent in ResolveTorrents(hashes))
        {
            torrent.SequentialDownload = enabled;
            _torrentService.Update(torrent);
        }

        return Content("Ok.", "text/plain");
    }

    [HttpPost("torrents/toggleFirstLastPiecePrio")]
    [HttpPost("torrents/setFirstLastPiecePrio")]
    public ActionResult SetFirstLastPiecePrio([FromForm] string hashes)
    {
        return Content("Ok.", "text/plain");
    }

    private List<Torrent> ResolveTorrents(string hashes)
    {
        if (string.IsNullOrWhiteSpace(hashes))
        {
            return new List<Torrent>();
        }

        if (string.Equals(hashes.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return _torrentService.GetAll().OrderBy(t => t.SortOrder).ToList();
        }

        var hashList = hashes.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(h => h.Trim().ToLowerInvariant())
            .ToHashSet();

        return _torrentService.GetAll()
            .Where(t => t.InfoHash != null && hashList.Contains(t.InfoHash.ToLowerInvariant()))
            .ToList();
    }

    private string GetClientSessionKey()
    {
        try
        {
            if (HttpContext != null && Request != null)
            {
                if (Request.Cookies != null && Request.Cookies.TryGetValue("SID", out var sid) && !string.IsNullOrWhiteSpace(sid))
                {
                    return $"sid:{sid}";
                }

                var apiKey = Request.Headers?["X-Api-Key"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(apiKey) && Request.Query != null && Request.Query.TryGetValue("apikey", out var qKey))
                {
                    apiKey = qKey.FirstOrDefault();
                }

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    var ip = HttpContext.Connection?.RemoteIpAddress?.ToString() ?? "unknown";
                    return $"key:{apiKey}:{ip}";
                }

                var remoteIp = HttpContext.Connection?.RemoteIpAddress?.ToString();
                if (!string.IsNullOrWhiteSpace(remoteIp))
                {
                    return $"ip:{remoteIp}";
                }
            }
        }
        catch
        {
            // Fallback
        }

        return "default_session";
    }

    private static void CleanupExpiredSessionSyncStates()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSyncCleanupTime < TimeSpan.FromMinutes(10))
        {
            return;
        }

        _lastSyncCleanupTime = now;
        var expirationThreshold = now.AddHours(-1);

        foreach (var kvp in _sessionSyncStates)
        {
            if (kvp.Value.LastAccessed < expirationThreshold)
            {
                _sessionSyncStates.TryRemove(kvp.Key, out _);
            }
        }
    }

    internal static string MapToQBitState(TorrentStatus status, double progress)
    {
        return status switch
        {
            TorrentStatus.Queued => progress >= 1.0 ? "queuedUP" : "queuedDL",
            TorrentStatus.Downloading => "downloading",
            TorrentStatus.Seeding => "uploading",
            TorrentStatus.Paused => progress >= 1.0 ? "pausedUP" : "pausedDL",
            TorrentStatus.Stopped => progress >= 1.0 ? "pausedUP" : "pausedDL",
            TorrentStatus.Error => "error",
            _ => "unknown",
        };
    }

    internal static long CalculateEta(Torrent t)
    {
        if (t == null)
        {
            return 8640000;
        }

        if (t.Progress >= 1.0 || t.Status == TorrentStatus.Seeding)
        {
            return 0;
        }

        if (t.Eta > 0)
        {
            return t.Eta;
        }

        if (t.DownloadSpeed > 0)
        {
            var remaining = Math.Max(0, t.TotalSize - t.Downloaded);
            if (remaining == 0 && t.TotalSize > 0 && t.Progress < 1.0)
            {
                remaining = (long)(t.TotalSize * (1.0 - t.Progress));
            }

            if (remaining > 0)
            {
                return (long)Math.Ceiling((double)remaining / t.DownloadSpeed);
            }

            return 0;
        }

        return 8640000;
    }
}

public record QBitTorrentSnapshot
{
    public string Name { get; init; } = string.Empty;
    public long Size { get; init; }
    public double Progress { get; init; }
    public long DlSpeed { get; init; }
    public long UpSpeed { get; init; }
    public string State { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Tags { get; init; } = string.Empty;
    public string SavePath { get; init; } = string.Empty;
    public string ContentPath { get; init; } = string.Empty;
    public long Eta { get; init; }
    public double Ratio { get; init; }
    public int NumSeeds { get; init; }
    public int NumLeechs { get; init; }
    public long Downloaded { get; init; }
    public long Uploaded { get; init; }
    public long AmountLeft { get; init; }
    public long AddedOn { get; init; }
    public long CompletionOn { get; init; }
    public bool SeqDl { get; init; }
    public bool FLPiecePrio { get; init; }

    public static QBitTorrentSnapshot FromTorrent(Torrent torrent, string savePath = "", string contentPath = "")
    {
        ArgumentNullException.ThrowIfNull(torrent);

        var addedOn = new DateTimeOffset(torrent.DateAdded).ToUnixTimeSeconds();
        var completionOn = torrent.Progress >= 1.0 ? new DateTimeOffset(torrent.LastActive ?? torrent.DateAdded).ToUnixTimeSeconds() : 0L;
        var amountLeft = Math.Max(0, torrent.TotalSize - torrent.Downloaded);

        return new QBitTorrentSnapshot
        {
            Name = torrent.Name ?? string.Empty,
            Size = torrent.TotalSize,
            Progress = torrent.Progress,
            DlSpeed = torrent.DownloadSpeed,
            UpSpeed = torrent.UploadSpeed,
            State = QBittorrentApiController.MapToQBitState(torrent.Status, torrent.Progress),
            Category = torrent.Label ?? string.Empty,
            Tags = torrent.Label ?? string.Empty,
            SavePath = savePath ?? string.Empty,
            ContentPath = contentPath ?? string.Empty,
            Eta = QBittorrentApiController.CalculateEta(torrent),
            Ratio = torrent.Ratio,
            NumSeeds = torrent.Seeders,
            NumLeechs = torrent.Leechers,
            Downloaded = torrent.Downloaded,
            Uploaded = torrent.Uploaded,
            AmountLeft = amountLeft,
            AddedOn = addedOn,
            CompletionOn = completionOn,
            SeqDl = torrent.SequentialDownload,
            FLPiecePrio = false,
        };
    }
}

public class QBitSessionSyncState
{
    public bool Initialized { get; set; }
    public int CurrentRid { get; set; }
    public Dictionary<string, (QBitTorrentSnapshot Snapshot, int ChangedAtRid)> CachedTorrents { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Hash, int RemovedAtRid)> RemovedTorrents { get; } = new();
    public object Lock { get; } = new();
    public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
}
