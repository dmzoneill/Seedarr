using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Http.Security;

namespace Seedarr.Api.V1.Transmission;

public class TransmissionRpcRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, JsonElement> Arguments { get; set; } = new();

    [JsonPropertyName("tag")]
    public JsonElement Tag { get; set; }
}

public class TransmissionRpcResponse
{
    [JsonPropertyName("result")]
    public string Result { get; set; } = "success";

    [JsonPropertyName("arguments")]
    public object Arguments { get; set; }

    [JsonPropertyName("tag")]
    public object Tag { get; set; }
}

[AllowAnonymous]
[ApiController]
[Route("transmission/rpc")]
public class TransmissionRpcController : ControllerBase, IHandle<TorrentDeletedEvent>
{
    public const string SessionHeaderName = "X-Transmission-Session-Id";
    private static string _currentSessionId = Guid.NewGuid().ToString("N");

    public static string CurrentSessionId
    {
        get => _currentSessionId;
        set => _currentSessionId = value;
    }

    private static readonly object _removedLock = new();
    private static readonly List<(int Id, DateTime RemovedAt)> _recentlyRemovedList = new();
    private static readonly DateTime _serviceStartTime = DateTime.UtcNow;
    private static readonly HttpClient _sharedHttpClient = new();

    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ITorrentImportService _torrentImportService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IConfigService _configService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly ITagService _tagService;
    private readonly HttpClient _httpClient;
    private readonly IRemotePathMappingService _remotePathMappingService;
    private readonly ICallerHostResolver _callerHostResolver;
    private readonly ICategoryService _categoryService;
    private readonly ITorrentRelocationService _relocationService;
    private readonly Logger _logger;

    public static void RecordRemovedId(int id)
    {
        lock (_removedLock)
        {
            _recentlyRemovedList.RemoveAll(x => x.Id == id || (DateTime.UtcNow - x.RemovedAt).TotalMinutes > 10);
            _recentlyRemovedList.Add((id, DateTime.UtcNow));
        }
    }

    public static List<int> GetRecentlyRemovedIds()
    {
        lock (_removedLock)
        {
            _recentlyRemovedList.RemoveAll(x => (DateTime.UtcNow - x.RemovedAt).TotalMinutes > 10);
            return _recentlyRemovedList.Select(x => x.Id).Distinct().ToList();
        }
    }

    public static void ClearRecentlyRemovedIds()
    {
        lock (_removedLock)
        {
            _recentlyRemovedList.Clear();
        }
    }

    [NonAction]
    public void Handle(TorrentDeletedEvent message)
    {
        if (message != null)
        {
            var torrentId = message.TorrentId > 0 ? message.TorrentId : message.Torrent?.Id ?? 0;
            if (torrentId > 0)
            {
                RecordRemovedId(torrentId);
            }
        }
    }

    private bool IsLocalOrWhitelisted()
    {
        var connection = HttpContext?.Connection;
        if (connection?.RemoteIpAddress == null)
        {
            return true;
        }

        if (IPAddress.IsLoopback(connection.RemoteIpAddress))
        {
            return true;
        }

        if (_configService != null)
        {
            var remoteIpStr = connection.RemoteIpAddress.ToString();
            if (remoteIpStr.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                remoteIpStr.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRecentlyActive(Dictionary<string, JsonElement> arguments)
    {
        if (arguments == null)
        {
            return false;
        }

        if (arguments.TryGetValue("ids", out var idsElem))
        {
            if (idsElem.ValueKind == JsonValueKind.String && string.Equals(idsElem.GetString(), "recently-active", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (idsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in idsElem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), "recently-active", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public TransmissionRpcController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ITorrentImportService torrentImportService,
        ITrackerEntryService trackerEntryService,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null,
        ITagService tagService = null,
        HttpClient httpClient = null,
        IRemotePathMappingService remotePathMappingService = null,
        ICallerHostResolver callerHostResolver = null,
        ICategoryService categoryService = null,
        ITorrentRelocationService relocationService = null)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _torrentImportService = torrentImportService;
        _trackerEntryService = trackerEntryService;
        _configService = configService;
        _configFileProvider = configFileProvider;
        _tagService = tagService;
        _httpClient = httpClient ?? _sharedHttpClient;
        _remotePathMappingService = remotePathMappingService;
        _callerHostResolver = callerHostResolver;
        _categoryService = categoryService;
        _relocationService = relocationService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    private string GetCallerHost()
    {
        return _callerHostResolver?.ResolveHost(HttpContext) ?? "localhost";
    }

    private string RemapRemoteToLocal(string path)
    {
        if (string.IsNullOrEmpty(path) || _remotePathMappingService == null)
        {
            return path;
        }

        return _remotePathMappingService.RemapRemoteToLocal(GetCallerHost(), path);
    }

    private string RemapLocalToRemote(string path)
    {
        if (string.IsNullOrEmpty(path) || _remotePathMappingService == null)
        {
            return path;
        }

        return _remotePathMappingService.RemapLocalToRemote(GetCallerHost(), path);
    }

    private IActionResult CreateSessionConflictResult()
    {
        Response.Headers[SessionHeaderName] = _currentSessionId;
        var result = Content("<h1>409: Conflict</h1><p>Your request had an invalid session-id header. Use the \"X-Transmission-Session-Id: " + _currentSessionId + "\" header to authenticate your request.</p>", "text/html");
        result.StatusCode = 409;
        return result;
    }

    private bool IsValidSessionId()
    {
        return Request.Headers.TryGetValue(SessionHeaderName, out var sessionVal) &&
            !string.IsNullOrEmpty(sessionVal) &&
            string.Equals(sessionVal, _currentSessionId, StringComparison.Ordinal);
    }

    [HttpGet]
    public IActionResult HandleGet()
    {
        if (!RpcAuthenticationHelper.IsAuthenticated(HttpContext, _configFileProvider))
        {
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"Transmission\"";
            return Unauthorized();
        }

        if (!IsValidSessionId())
        {
            return CreateSessionConflictResult();
        }

        return Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = new Dictionary<string, object>
            {
                { "version", "3.00 (Seedarr)" },
                { "rpc-version", 17 },
                { "rpc-version-minimum", 1 }
            },
        });
    }

    [HttpPost]
    public async Task<IActionResult> HandleRpc([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] TransmissionRpcRequest request = null)
    {
        if (!RpcAuthenticationHelper.IsAuthenticated(HttpContext, _configFileProvider))
        {
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"Transmission\"";
            return Unauthorized();
        }

        if (!IsValidSessionId())
        {
            return CreateSessionConflictResult();
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Method))
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = null });
        }

        object tag = null;
        if (request.Tag.ValueKind != JsonValueKind.Undefined && request.Tag.ValueKind != JsonValueKind.Null)
        {
            if (request.Tag.ValueKind == JsonValueKind.Number && request.Tag.TryGetInt64(out var tagNum))
            {
                tag = tagNum;
            }
            else if (request.Tag.ValueKind == JsonValueKind.String)
            {
                tag = request.Tag.GetString();
            }
            else
            {
                tag = request.Tag;
            }
        }

        try
        {
            var method = request.Method.ToLowerInvariant();
            return method switch
            {
                "session-get" => HandleSessionGet(tag),
                "session-set" => HandleSessionSet(request, tag),
                "session-stats" => HandleSessionStats(tag),
                "session-close" => HandleSessionClose(tag),
                "torrent-get" => HandleTorrentGet(request, tag),
                "torrent-add" => await HandleTorrentAddAsync(request, tag),
                "torrent-set" => HandleTorrentSet(request, tag),
                "torrent-set-location" => HandleTorrentSetLocation(request, tag),
                "free-space" => HandleFreeSpace(request, tag),
                "queue-move-top" => HandleQueueMove(request, tag, "top"),
                "queue-move-up" => HandleQueueMove(request, tag, "up"),
                "queue-move-down" => HandleQueueMove(request, tag, "down"),
                "queue-move-bottom" => HandleQueueMove(request, tag, "bottom"),
                "torrent-start" or "torrent-start-now" => HandleTorrentStart(request, tag),
                "torrent-stop" => HandleTorrentStop(request, tag),
                "torrent-verify" => HandleTorrentVerify(request, tag),
                "torrent-reannounce" => HandleTorrentReannounce(request, tag),
                "torrent-remove" => HandleTorrentRemove(request, tag),
                "torrent-rename-path" => HandleTorrentRenamePath(request, tag),
                "port-test" => HandlePortTest(tag),
                "blocklist-update" => HandleBlocklistUpdate(tag),
                _ => HandleUnknownMethod(request, tag),
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Transmission RPC method: {0}", request.Method);
            return Ok(new TransmissionRpcResponse { Result = ex.Message, Tag = tag });
        }
    }

    private IActionResult HandleSessionGet(object tag)
    {
        return Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = new Dictionary<string, object>
            {
                { "version", "3.00 (Seedarr)" },
                { "rpc-version", 17 },
                { "rpc-version-minimum", 1 },
                { "download-dir", RemapLocalToRemote(_configService?.WatchFolderPath ?? "/downloads") },
                { "incomplete-dir", "/downloads/incomplete" },
                { "incomplete-dir-enabled", false },
                { "speed-limit-down", _configService?.MaxDownloadSpeedKbps ?? 1250 },
                { "speed-limit-up", _configService?.MaxUploadSpeedKbps ?? 625 },
                { "speed-limit-down-enabled", _configService?.SpeedLimitDownEnabled ?? false },
                { "speed-limit-up-enabled", _configService?.SpeedLimitUpEnabled ?? false },
                { "seedRatioLimit", _configService?.GlobalSeedRatioLimit ?? 0.0 },
                { "seedRatioLimited", _configService?.SeedRatioLimited ?? false },
                { "alt-speed-enabled", _configService?.AlternativeSpeedEnabled ?? false },
                { "alt-speed-down", _configService?.AltDownloadSpeedKbps ?? 100 },
                { "alt-speed-up", _configService?.AltUploadSpeedKbps ?? 50 },
                { "peer-port", _configService?.ListeningPort ?? 6881 },
                { "blocklist-enabled", false },
                { "blocklist-size", 0 },
                { "blocklist-url", string.Empty },
                { "script-torrent-done-filename", string.Empty },
                { "script-torrent-done-enabled", false },
                { "script-torrent-added-filename", string.Empty },
                { "script-torrent-added-enabled", false },
                { "script-torrent-done-seeding-filename", string.Empty },
                { "script-torrent-done-seeding-enabled", false },
            },
            Tag = tag,
        });
    }

    private IActionResult HandleSessionSet(TransmissionRpcRequest request, object tag)
    {
        if (request.Arguments != null && _configService != null)
        {
            var updates = new Dictionary<string, object>();

            if (request.Arguments.TryGetValue("download-dir", out var dlDir) && dlDir.ValueKind == JsonValueKind.String)
            {
                updates["WatchFolderPath"] = RemapRemoteToLocal(dlDir.GetString());
            }

            if (request.Arguments.TryGetValue("speed-limit-down", out var dlLimit) && dlLimit.ValueKind == JsonValueKind.Number)
            {
                updates["MaxDownloadSpeedKbps"] = dlLimit.GetInt32();
            }

            if (request.Arguments.TryGetValue("speed-limit-up", out var upLimit) && upLimit.ValueKind == JsonValueKind.Number)
            {
                updates["MaxUploadSpeedKbps"] = upLimit.GetInt32();
            }

            if (request.Arguments.TryGetValue("speed-limit-down-enabled", out var dlLimitEnabled))
            {
                updates["SpeedLimitDownEnabled"] = SafeGetBoolean(dlLimitEnabled);
            }

            if (request.Arguments.TryGetValue("speed-limit-up-enabled", out var upLimitEnabled))
            {
                updates["SpeedLimitUpEnabled"] = SafeGetBoolean(upLimitEnabled);
            }

            if (request.Arguments.TryGetValue("seedRatioLimit", out var ratioLimit))
            {
                if (ratioLimit.ValueKind == JsonValueKind.Number && ratioLimit.TryGetDouble(out var rl))
                {
                    updates["GlobalSeedRatioLimit"] = rl;
                }
                else if (ratioLimit.ValueKind == JsonValueKind.String && double.TryParse(ratioLimit.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedRl))
                {
                    updates["GlobalSeedRatioLimit"] = parsedRl;
                }
            }

            if (request.Arguments.TryGetValue("seedRatioLimited", out var ratioLimited))
            {
                var isLimited = SafeGetBoolean(ratioLimited);
                updates["SeedRatioLimited"] = isLimited;
                updates["GlobalSeedRatioLimited"] = isLimited;
            }

            if (request.Arguments.TryGetValue("alt-speed-down", out var altDl) && altDl.ValueKind == JsonValueKind.Number)
            {
                updates["AltDownloadSpeedKbps"] = altDl.GetInt32();
            }

            if (request.Arguments.TryGetValue("alt-speed-up", out var altUp) && altUp.ValueKind == JsonValueKind.Number)
            {
                updates["AltUploadSpeedKbps"] = altUp.GetInt32();
            }

            if (request.Arguments.TryGetValue("alt-speed-enabled", out var altEn))
            {
                updates["AlternativeSpeedEnabled"] = SafeGetBoolean(altEn);
            }

            if (request.Arguments.TryGetValue("peer-port", out var peerPort) && peerPort.ValueKind == JsonValueKind.Number)
            {
                updates["ListeningPort"] = peerPort.GetInt32();
            }

            if (updates.Count > 0)
            {
                _configService.SaveConfigDictionary(updates);
            }
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleSessionStats(object tag)
    {
        var allTorrents = _torrentService.GetAll();
        var activeTorrents = allTorrents.Count(t => t.Status == TorrentStatus.Downloading || t.Status == TorrentStatus.Seeding);
        var pausedTorrents = allTorrents.Count(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped);
        var totalDownloaded = allTorrents.Sum(t => t.Downloaded);
        var totalUploaded = allTorrents.Sum(t => t.Uploaded);
        var secondsActive = (long)Math.Max(0, (DateTime.UtcNow - _serviceStartTime).TotalSeconds);

        return Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = new Dictionary<string, object>
            {
                { "activeTorrentCount", activeTorrents },
                { "downloadSpeed", allTorrents.Sum(t => t.DownloadSpeed) },
                { "pausedTorrentCount", pausedTorrents },
                { "torrentCount", allTorrents.Count },
                { "uploadSpeed", allTorrents.Sum(t => t.UploadSpeed) },
                {
                    "cumulative-stats", new Dictionary<string, object>
                    {
                        { "downloadedBytes", totalDownloaded },
                        { "filesAdded", allTorrents.Count },
                        { "secondsActive", secondsActive },
                        { "sessionCount", 1 },
                        { "uploadedBytes", totalUploaded },
                    }
                },
                {
                    "current-stats", new Dictionary<string, object>
                    {
                        { "downloadedBytes", totalDownloaded },
                        { "filesAdded", allTorrents.Count },
                        { "secondsActive", secondsActive },
                        { "sessionCount", 1 },
                        { "uploadedBytes", totalUploaded },
                    }
                },
            },
            Tag = tag,
        });
    }

    private IActionResult HandleTorrentGet(TransmissionRpcRequest request, object tag)
    {
        var isRecentlyActive = IsRecentlyActive(request.Arguments);
        var torrents = _torrentService.GetAll().AsEnumerable();
        var targetIds = ExtractIds(request.Arguments, false);

        if (targetIds.Count > 0)
        {
            var targetIdSet = targetIds.ToHashSet();
            torrents = torrents.Where(t => targetIdSet.Contains(t.Id));
        }
        else if (isRecentlyActive)
        {
            var recentCutoff = DateTime.UtcNow.AddMinutes(-5);
            torrents = torrents.Where(t =>
                t.Status == TorrentStatus.Downloading ||
                t.Status == TorrentStatus.Seeding ||
                t.DownloadSpeed > 0 ||
                t.UploadSpeed > 0 ||
                t.Active ||
                (t.LastActive.HasValue && t.LastActive.Value >= recentCutoff));
        }

        HashSet<string> requestedFields = null;
        if (request.Arguments != null && request.Arguments.TryGetValue("fields", out var fieldsVal) && fieldsVal.ValueKind == JsonValueKind.Array)
        {
            requestedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fieldsVal.EnumerateArray())
            {
                if (f.ValueKind == JsonValueKind.String)
                {
                    requestedFields.Add(f.GetString());
                }
            }
        }

        var mappedTorrents = torrents.Select(t => MapTorrentToTransmission(t, requestedFields)).ToList();
        var responseArgs = new Dictionary<string, object>
        {
            { "torrents", mappedTorrents },
        };

        if (isRecentlyActive)
        {
            responseArgs["removed"] = GetRecentlyRemovedIds();
        }

        return Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = responseArgs,
            Tag = tag,
        });
    }

    private IActionResult HandleTorrentSet(TransmissionRpcRequest request, object tag)
    {
        var setIds = ExtractIds(request.Arguments, false);
        if (setIds.Count == 0)
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
        }

        foreach (var id in setIds)
        {
            var t = _torrentService.Get(id);
            if (t != null)
            {
                if (request.Arguments.TryGetValue("bandwidthPriority", out var bpVal) && bpVal.ValueKind == JsonValueKind.Number)
                {
                    t.Priority = bpVal.GetInt32();
                }

                if (request.Arguments.TryGetValue("labels", out var lblVal) && lblVal.ValueKind == JsonValueKind.Array)
                {
                    var lbls = lblVal.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    if (lbls.Count > 0)
                    {
                        t.Label = string.Join(",", lbls);
                        if (_tagService != null)
                        {
                            t.TagIds = _tagService.SyncTagsFromLabels(lbls);
                        }

                        if (_categoryService != null)
                        {
                            var matchedCat = lbls
                                .Select(l => _categoryService.GetByName(l))
                                .FirstOrDefault(c => c != null && !string.IsNullOrWhiteSpace(c.Name));

                            t.Category = matchedCat?.Name ?? lbls[0];
                        }
                        else
                        {
                            t.Category = lbls[0];
                        }
                    }
                    else
                    {
                        t.Label = string.Empty;
                        t.Category = string.Empty;
                        t.TagIds = new List<int>();
                    }
                }

                if (request.Arguments.TryGetValue("downloadLimit", out var dlLimitVal) && dlLimitVal.ValueKind == JsonValueKind.Number)
                {
                    t.DownloadLimit = dlLimitVal.GetInt32();
                }

                if (request.Arguments.TryGetValue("uploadLimit", out var ulLimitVal) && ulLimitVal.ValueKind == JsonValueKind.Number)
                {
                    t.UploadLimit = ulLimitVal.GetInt32();
                }

                if (request.Arguments.TryGetValue("location", out var locVal) && locVal.ValueKind == JsonValueKind.String)
                {
                    var targetLocation = locVal.GetString();
                    if (!string.IsNullOrWhiteSpace(targetLocation))
                    {
                        t.SourcePath = RemapRemoteToLocal(targetLocation);
                    }
                }

                if (request.Arguments.TryGetValue("trackerAdd", out var trackerAddVal) && trackerAddVal.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in trackerAddVal.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var url = item.GetString();
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                var existing = _trackerEntryService.GetByTorrentId(t.Id).FirstOrDefault(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
                                if (existing == null)
                                {
                                    _trackerEntryService.Add(new TrackerEntry
                                    {
                                        TorrentId = t.Id,
                                        Url = url,
                                        Tier = 0,
                                        Enabled = true,
                                        Status = TrackerStatus.Working,
                                    });
                                }
                            }
                        }
                    }
                }

                if (request.Arguments.TryGetValue("seedRatioLimit", out var srlVal) && srlVal.ValueKind == JsonValueKind.Number)
                {
                    t.RatioLimit = srlVal.GetDouble();
                }

                if (request.Arguments.TryGetValue("seedRatioMode", out var srmVal) && srmVal.ValueKind == JsonValueKind.Number)
                {
                    var mode = srmVal.GetInt32();
                    if (mode == 0)
                    {
                        t.RatioLimit = null;
                    }
                    else if (mode == 2)
                    {
                        t.RatioLimit = -1;
                    }
                }

                if (request.Arguments.TryGetValue("seedIdleLimit", out var silVal) && silVal.ValueKind == JsonValueKind.Number)
                {
                    t.SeedingTimeLimit = silVal.GetInt32();
                }

                if (request.Arguments.TryGetValue("seedIdleMode", out var simVal) && simVal.ValueKind == JsonValueKind.Number)
                {
                    var mode = simVal.GetInt32();
                    if (mode == 0)
                    {
                        t.SeedingTimeLimit = null;
                    }
                    else if (mode == 2)
                    {
                        t.SeedingTimeLimit = -1;
                    }
                }

                var files = _torrentFileService.GetByTorrentId(t.Id);
                if (files != null && files.Count > 0)
                {
                    var modifiedFiles = new HashSet<TorrentFile>();

                    if (request.Arguments.TryGetValue("files-wanted", out var fwVal) && fwVal.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var idx in fwVal.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()))
                        {
                            if (idx >= 0 && idx < files.Count)
                            {
                                files[idx].Wanted = true;
                                modifiedFiles.Add(files[idx]);
                            }
                        }
                    }

                    if (request.Arguments.TryGetValue("files-unwanted", out var fuVal) && fuVal.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var idx in fuVal.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()))
                        {
                            if (idx >= 0 && idx < files.Count)
                            {
                                files[idx].Wanted = false;
                                modifiedFiles.Add(files[idx]);
                            }
                        }
                    }

                    if (request.Arguments.TryGetValue("priority-high", out var phVal) && phVal.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var idx in phVal.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()))
                        {
                            if (idx >= 0 && idx < files.Count)
                            {
                                files[idx].Priority = 1;
                                modifiedFiles.Add(files[idx]);
                            }
                        }
                    }

                    if (request.Arguments.TryGetValue("priority-low", out var plVal) && plVal.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var idx in plVal.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()))
                        {
                            if (idx >= 0 && idx < files.Count)
                            {
                                files[idx].Priority = -1;
                                modifiedFiles.Add(files[idx]);
                            }
                        }
                    }

                    if (request.Arguments.TryGetValue("priority-normal", out var pnVal) && pnVal.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var idx in pnVal.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()))
                        {
                            if (idx >= 0 && idx < files.Count)
                            {
                                files[idx].Priority = 0;
                                modifiedFiles.Add(files[idx]);
                            }
                        }
                    }

                    foreach (var file in modifiedFiles)
                    {
                        _torrentFileService.Update(file);
                    }
                }

                _torrentService.Update(t);
            }
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Transmission RPC relocates torrent paths")]
    private IActionResult HandleTorrentSetLocation(TransmissionRpcRequest request, object tag)
    {
        var locIds = ExtractIds(request.Arguments, false);
        var newLocation = request.Arguments != null && request.Arguments.TryGetValue("location", out var locElem)
            ? locElem.GetString()
            : null;
        var shouldMove = request.Arguments != null && request.Arguments.TryGetValue("move", out var moveElem) && SafeGetBoolean(moveElem);

        if (!string.IsNullOrWhiteSpace(newLocation))
        {
            var remappedLocation = RemapRemoteToLocal(newLocation);
            foreach (var id in locIds)
            {
                var t = _torrentService.Get(id);
                if (t != null)
                {
                    if (shouldMove && _relocationService != null)
                    {
                        _ = _relocationService.RelocateTorrentAsync(id, remappedLocation);
                    }
                    else
                    {
                        t.SourcePath = remappedLocation;
                        _torrentService.Update(t);
                    }
                }
            }
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Transmission RPC intentionally imports torrent files from local filesystem paths")]
    private async Task<IActionResult> HandleTorrentAddAsync(TransmissionRpcRequest request, object tag)
    {
        var filename = request.Arguments != null && request.Arguments.TryGetValue("filename", out var f) ? f.GetString() : null;
        var metainfo = request.Arguments != null && request.Arguments.TryGetValue("metainfo", out var m) ? m.GetString() : null;
        var paused = request.Arguments != null && request.Arguments.TryGetValue("paused", out var p) && SafeGetBoolean(p);
        var downloadDir = request.Arguments != null && request.Arguments.TryGetValue("download-dir", out var dd) ? dd.GetString() : null;
        var cookies = request.Arguments != null && request.Arguments.TryGetValue("cookies", out var c) ? c.GetString() : null;

        List<string> labels = null;
        if (request.Arguments != null && request.Arguments.TryGetValue("labels", out var lblVal) && lblVal.ValueKind == JsonValueKind.Array)
        {
            labels = lblVal.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        int? bandwidthPriority = null;
        if (request.Arguments != null && request.Arguments.TryGetValue("bandwidthPriority", out var bpVal) && bpVal.ValueKind == JsonValueKind.Number)
        {
            bandwidthPriority = bpVal.GetInt32();
        }

        Torrent added = null;
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

        try
        {
            if (!string.IsNullOrWhiteSpace(metainfo))
            {
                var bytes = Convert.FromBase64String(metainfo);
                using var ms = new MemoryStream(bytes);
                added = _torrentImportService.ImportFromFile(ms, "metainfo.torrent");
            }
            else if (!string.IsNullOrWhiteSpace(filename))
            {
                if (filename.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                {
                    added = _torrentImportService.ImportFromMagnet(filename);
                }
                else if (filename.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || filename.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(30));

                    using var reqMsg = new HttpRequestMessage(HttpMethod.Get, filename);
                    if (!string.IsNullOrWhiteSpace(cookies))
                    {
                        reqMsg.Headers.TryAddWithoutValidation("Cookie", cookies);
                    }

                    using var resp = await _httpClient.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    resp.EnsureSuccessStatusCode();

                    const long maxTorrentFileSize = 20 * 1024 * 1024; // 20 MB
                    if (resp.Content.Headers.ContentLength > maxTorrentFileSize)
                    {
                        return Ok(new TransmissionRpcResponse { Result = "torrent file exceeds maximum allowed size", Tag = tag });
                    }

                    var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
                    if (bytes.Length > maxTorrentFileSize)
                    {
                        return Ok(new TransmissionRpcResponse { Result = "torrent file exceeds maximum allowed size", Tag = tag });
                    }

                    using var ms = new MemoryStream(bytes);
                    added = _torrentImportService.ImportFromFile(ms, "downloaded.torrent");
                }
                else
                {
                    var localPath = filename;
                    if (localPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(localPath, UriKind.Absolute, out var fileUri))
                    {
                        localPath = fileUri.LocalPath;
                    }

                    if (global::System.IO.File.Exists(localPath))
                    {
                        var bytes = await global::System.IO.File.ReadAllBytesAsync(localPath, cancellationToken);
                        var fileName = Path.GetFileName(localPath);
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            fileName = "file.torrent";
                        }

                        using var ms = new MemoryStream(bytes);
                        added = _torrentImportService.ImportFromFile(ms, fileName);
                    }
                }
            }

            if (added != null)
            {
                var needsUpdate = false;
                string matchingCategory = null;

                if (labels != null && labels.Count > 0)
                {
                    added.Label = string.Join(",", labels);
                    if (_tagService != null)
                    {
                        added.TagIds = _tagService.SyncTagsFromLabels(labels);
                    }

                    if (_categoryService != null)
                    {
                        var matchedCat = labels
                            .Select(l => _categoryService.GetByName(l))
                            .FirstOrDefault(c => c != null && !string.IsNullOrWhiteSpace(c.Name));

                        if (matchedCat != null)
                        {
                            matchingCategory = matchedCat.Name;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(matchingCategory))
                    {
                        matchingCategory = labels[0];
                    }

                    added.Category = matchingCategory;
                    needsUpdate = true;
                }

                if (!string.IsNullOrWhiteSpace(downloadDir))
                {
                    added.SourcePath = RemapRemoteToLocal(downloadDir);
                    needsUpdate = true;
                }
                else if (_categoryService != null)
                {
                    var defaultPath = _configService?.WatchFolderPath ?? "/downloads";
                    var resolvedPath = _categoryService.GetSavePathForCategory(matchingCategory ?? string.Empty, defaultPath);
                    if (!string.IsNullOrWhiteSpace(resolvedPath))
                    {
                        added.SourcePath = resolvedPath;
                        needsUpdate = true;
                    }
                }

                if (bandwidthPriority.HasValue)
                {
                    added.Priority = bandwidthPriority.Value;
                    needsUpdate = true;
                }

                if (paused)
                {
                    added.Pause();
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    _torrentService.Update(added);
                }

                return Ok(new TransmissionRpcResponse
                {
                    Result = "success",
                    Arguments = new Dictionary<string, object>
                    {
                        ["torrent-added"] = new Dictionary<string, object>
                        {
                            ["id"] = added.Id,
                            ["name"] = added.Name,
                            ["hashString"] = (added.InfoHash ?? string.Empty).ToLowerInvariant(),
                        }
                    },
                    Tag = tag,
                });
            }
        }
        catch (Exception ex) when (ex is DuplicateTorrentException ||
            ex.InnerException is DuplicateTorrentException ||
            (ex is InvalidOperationException && ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)))
        {
            var targetHash = TryExtractInfoHash(filename, metainfo);

            if (string.IsNullOrWhiteSpace(targetHash) && request.Arguments != null)
            {
                if (request.Arguments.TryGetValue("hash", out var h) && h.ValueKind == JsonValueKind.String)
                {
                    targetHash = h.GetString();
                }
                else if (request.Arguments.TryGetValue("infoHash", out var ih) && ih.ValueKind == JsonValueKind.String)
                {
                    targetHash = ih.GetString();
                }
                else if (request.Arguments.TryGetValue("hashString", out var hs) && hs.ValueKind == JsonValueKind.String)
                {
                    targetHash = hs.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(targetHash))
            {
                if (ex is DuplicateTorrentException dtex && !string.IsNullOrWhiteSpace(dtex.InfoHash))
                {
                    targetHash = dtex.InfoHash.ToLowerInvariant();
                }
                else if (ex.InnerException is DuplicateTorrentException innerDtex && !string.IsNullOrWhiteSpace(innerDtex.InfoHash))
                {
                    targetHash = innerDtex.InfoHash.ToLowerInvariant();
                }
                else
                {
                    var match = Regex.Match(ex.Message, @"\b[0-9a-fA-F]{40}\b");
                    if (match.Success)
                    {
                        targetHash = match.Value.ToLowerInvariant();
                    }
                }
            }

            Torrent existing = null;
            if (!string.IsNullOrWhiteSpace(targetHash))
            {
                existing = _torrentService.GetByInfoHash(targetHash)
                    ?? _torrentService.GetAll()?.FirstOrDefault(t => string.Equals(t.InfoHash, targetHash, StringComparison.OrdinalIgnoreCase));
            }

            if (existing == null)
            {
                existing = _torrentService.GetAll()?.FirstOrDefault();
            }

            return Ok(new TransmissionRpcResponse
            {
                Result = "success",
                Arguments = new Dictionary<string, object>
                {
                    ["torrent-duplicate"] = new Dictionary<string, object>
                    {
                        ["id"] = existing?.Id ?? 1,
                        ["name"] = existing?.Name ?? "Torrent",
                        ["hashString"] = (existing?.InfoHash ?? targetHash ?? string.Empty).ToLowerInvariant(),
                    }
                },
                Tag = tag,
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add torrent in Transmission RPC");
            return Ok(new TransmissionRpcResponse { Result = ex.Message, Tag = tag });
        }

        return Ok(new TransmissionRpcResponse { Result = "invalid or missing torrent", Tag = tag });
    }

    [SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Transmission RPC intentionally imports torrent files from local filesystem paths")]
    private string TryExtractInfoHash(string filename, string metainfo)
    {
        if (!string.IsNullOrWhiteSpace(filename) && filename.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var parsed = MagnetLinkParser.Parse(filename);
                if (!string.IsNullOrWhiteSpace(parsed?.InfoHash))
                {
                    return parsed.InfoHash.ToLowerInvariant();
                }
            }
            catch
            {
                // ignored
            }
        }

        if (!string.IsNullOrWhiteSpace(metainfo))
        {
            try
            {
                var bytes = Convert.FromBase64String(metainfo);
                ParsedTorrent parsed = null;
                try
                {
                    parsed = _torrentFileParser?.Parse(bytes);
                }
                catch
                {
                    // ignored
                }

                if (parsed == null)
                {
                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        parsed = _torrentFileParser?.Parse(ms);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (!string.IsNullOrWhiteSpace(parsed?.InfoHash))
                {
                    return parsed.InfoHash.ToLowerInvariant();
                }
            }
            catch
            {
                // ignored
            }
        }

        if (!string.IsNullOrWhiteSpace(filename))
        {
            if (filename.Length == 40 && filename.All(Uri.IsHexDigit))
            {
                return filename.ToLowerInvariant();
            }

            try
            {
                var localPath = filename;
                if (localPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(localPath, UriKind.Absolute, out var fileUri))
                {
                    localPath = fileUri.LocalPath;
                }

                if (global::System.IO.File.Exists(localPath))
                {
                    var parsed = _torrentFileParser?.Parse(localPath);
                    if (!string.IsNullOrWhiteSpace(parsed?.InfoHash))
                    {
                        return parsed.InfoHash.ToLowerInvariant();
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        return null;
    }

    private IActionResult HandleFreeSpace(TransmissionRpcRequest request, object tag)
    {
        var targetPath = request.Arguments != null && request.Arguments.TryGetValue("path", out var p) ? p.GetString() : (_configService?.WatchFolderPath ?? "/downloads");
        var freeBytes = GetFreeDiskSpace(targetPath);

        return Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = new Dictionary<string, object>
            {
                ["path"] = targetPath ?? "/downloads",
                ["size-bytes"] = freeBytes,
            },
            Tag = tag,
        });
    }

    private static long GetFreeDiskSpace(string path, long fallback = 100L * 1024 * 1024 * 1024)
    {
        try
        {
            var targetPath = !string.IsNullOrWhiteSpace(path) ? path : "/";
            var fullPath = Path.GetFullPath(targetPath);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                root = fullPath;
            }

            var drive = new DriveInfo(root);
            if (drive.IsReady)
            {
                return drive.AvailableFreeSpace;
            }
        }
        catch
        {
            // Drive lookup failed; try current directory root or fallback.
        }

        try
        {
            var rootDrive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "/");
            if (rootDrive.IsReady)
            {
                return rootDrive.AvailableFreeSpace;
            }
        }
        catch
        {
            // Fallback to default.
        }

        return fallback;
    }

    private IActionResult HandleQueueMove(TransmissionRpcRequest request, object tag, string position)
    {
        var ids = ExtractIds(request.Arguments, false);
        if (ids.Count == 0)
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
        }

        _torrentService.BatchMoveQueue(ids, position);

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentStart(TransmissionRpcRequest request, object tag)
    {
        var ids = ExtractIds(request.Arguments, false);
        if (ids.Count == 0)
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
        }

        foreach (var id in ids)
        {
            var t = _torrentService.Get(id);
            if (t != null)
            {
                t.Resume();
                _torrentService.Update(t);
            }
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentStop(TransmissionRpcRequest request, object tag)
    {
        var ids = ExtractIds(request.Arguments, false);
        if (ids.Count == 0)
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
        }

        foreach (var id in ids)
        {
            var t = _torrentService.Get(id);
            if (t != null)
            {
                t.Stop();
                _torrentService.Update(t);
            }
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentVerify(TransmissionRpcRequest request, object tag)
    {
        var ids = ExtractIds(request.Arguments, false);
        if (ids.Count == 0)
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
        }

        foreach (var id in ids)
        {
            _torrentService.Recheck(id);
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentReannounce(TransmissionRpcRequest request, object tag)
    {
        var ids = ExtractIds(request.Arguments, false);
        if (ids.Count == 0)
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
        }

        foreach (var id in ids)
        {
            var t = _torrentService.Get(id);
            if (t != null)
            {
                t.LastActive = DateTime.UtcNow;
                _torrentService.Update(t);
            }
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentRemove(TransmissionRpcRequest request, object tag)
    {
        var ids = ExtractIds(request.Arguments, false);
        if (ids.Count == 0)
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
        }

        var deleteData = request.Arguments != null && request.Arguments.TryGetValue("delete-local-data", out var d) && SafeGetBoolean(d);

        foreach (var id in ids)
        {
            RecordRemovedId(id);
            _torrentService.Delete(id, deleteData);
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentRenamePath(TransmissionRpcRequest request, object tag)
    {
        var ids = ExtractIds(request.Arguments, false);
        if (ids.Count == 0)
        {
            return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
        }

        string path = null;
        if (request.Arguments != null && request.Arguments.TryGetValue("path", out var p))
        {
            path = p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
        }

        string name = null;
        if (request.Arguments != null && request.Arguments.TryGetValue("name", out var n))
        {
            name = n.ValueKind == JsonValueKind.String ? n.GetString() : n.ToString();
        }

        var t = _torrentService.Get(ids[0]);
        if (t != null && !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(name))
        {
            var normalizedPath = path.Replace('\\', '/').Trim('/');
            var cleanName = name.Replace('\\', '/').Trim('/');
            var files = _torrentFileService.GetByTorrentId(t.Id);

            var exactMatch = files.FirstOrDefault(f => string.Equals(f.Path?.Replace('\\', '/').Trim('/'), normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                var lastSlash = normalizedPath.LastIndexOf('/');
                exactMatch.Path = lastSlash >= 0 ? $"{normalizedPath.Substring(0, lastSlash)}/{cleanName}" : cleanName;
                _torrentFileService.Update(exactMatch);
            }
            else
            {
                var folderPrefix = normalizedPath + "/";
                var lastSlash = normalizedPath.LastIndexOf('/');
                var parentDir = lastSlash >= 0 ? normalizedPath.Substring(0, lastSlash) : null;
                var newFolderPath = parentDir != null ? $"{parentDir}/{cleanName}" : cleanName;

                foreach (var file in files)
                {
                    var normalizedFilePath = file.Path?.Replace('\\', '/').Trim('/') ?? string.Empty;
                    if (normalizedFilePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var relativePart = normalizedFilePath.Substring(folderPrefix.Length);
                        file.Path = $"{newFolderPath}/{relativePart}";
                        _torrentFileService.Update(file);
                    }
                }
            }

            _torrentService.Recheck(t.Id);
        }

        return Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = new Dictionary<string, object>
            {
                ["path"] = path,
                ["name"] = name,
                ["id"] = t?.Id ?? ids[0],
            },
            Tag = tag,
        });
    }

    private IActionResult HandlePortTest(object tag)
    {
        return Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = new Dictionary<string, object>
            {
                ["port-is-open"] = true,
            },
            Tag = tag,
        });
    }

    private IActionResult HandleBlocklistUpdate(object tag)
    {
        return Ok(new TransmissionRpcResponse
        {
            Result = "success",
            Arguments = new Dictionary<string, object>
            {
                ["blocklist-size"] = 0,
            },
            Tag = tag,
        });
    }

    private IActionResult HandleSessionClose(object tag)
    {
        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleUnknownMethod(TransmissionRpcRequest request, object tag)
    {
        _logger.Warn("Unhandled Transmission RPC method: {0}", request.Method);
        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    [NonAction]
    public Dictionary<string, object> MapTorrentToTransmission(Torrent t, HashSet<string> fields)
    {
        var totalSize = t.TotalSize;
        var downloaded = t.Downloaded;

        var needFiles = fields == null || fields.Count == 0 || fields.Contains("files") || fields.Contains("fileStats") || fields.Contains("sizeWhenDone") || fields.Contains("leftUntilDone") || fields.Contains("priorities") || fields.Contains("wanted");
        var needTrackers = fields == null || fields.Count == 0 || fields.Contains("trackers") || fields.Contains("trackerStats");

        var files = needFiles ? _torrentFileService.GetByTorrentId(t.Id) : new List<TorrentFile>();
        var sizeWhenDone = files.Count > 0 ? files.Where(f => f.Wanted).Sum(f => f.Size) : totalSize;
        var leftUntilDone = Math.Max(0L, sizeWhenDone - downloaded);

        var trackers = new List<Dictionary<string, object>>();
        var trackerStats = new List<Dictionary<string, object>>();
        if (needTrackers)
        {
            var trkList = _trackerEntryService.GetByTorrentId(t.Id);
            trackers = trkList.Select(tr => new Dictionary<string, object>
            {
                ["id"] = tr.Id,
                ["announce"] = tr.Url ?? string.Empty,
                ["scrape"] = tr.Url ?? string.Empty,
                ["tier"] = tr.Tier,
            }).ToList();

            trackerStats = trkList.Select(tr =>
            {
                var host = string.Empty;
                if (!string.IsNullOrWhiteSpace(tr.Url) && Uri.TryCreate(tr.Url, UriKind.Absolute, out var uri))
                {
                    host = uri.Host;
                }

                return new Dictionary<string, object>
                {
                    ["id"] = tr.Id,
                    ["announce"] = tr.Url ?? string.Empty,
                    ["scrape"] = tr.Url ?? string.Empty,
                    ["tier"] = tr.Tier,
                    ["host"] = host,
                    ["seederCount"] = tr.Seeders,
                    ["leecherCount"] = tr.Leechers,
                    ["downloadCount"] = tr.Downloaded,
                    ["hasAnnounced"] = tr.LastAnnounce != null,
                    ["hasScraped"] = tr.LastScrape != null,
                    ["lastAnnounceSucceeded"] = tr.Status == TrackerStatus.Working,
                    ["lastAnnounceTime"] = tr.LastAnnounce != null ? new DateTimeOffset(tr.LastAnnounce.Value).ToUnixTimeSeconds() : 0L,
                    ["lastScrapeTime"] = tr.LastScrape != null ? new DateTimeOffset(tr.LastScrape.Value).ToUnixTimeSeconds() : 0L,
                    ["nextAnnounceTime"] = tr.NextAnnounce != null ? new DateTimeOffset(tr.NextAnnounce.Value).ToUnixTimeSeconds() : 0L,
                };
            }).ToList();

            if (trackers.Count == 0 && !string.IsNullOrWhiteSpace(t.TrackerUrl))
            {
                trackers.Add(new Dictionary<string, object>
                {
                    ["id"] = 1,
                    ["announce"] = t.TrackerUrl,
                    ["scrape"] = t.TrackerUrl,
                    ["tier"] = 0,
                });

                var fallbackHost = string.Empty;
                if (Uri.TryCreate(t.TrackerUrl, UriKind.Absolute, out var uri))
                {
                    fallbackHost = uri.Host;
                }

                trackerStats.Add(new Dictionary<string, object>
                {
                    ["id"] = 1,
                    ["announce"] = t.TrackerUrl,
                    ["scrape"] = t.TrackerUrl,
                    ["tier"] = 0,
                    ["host"] = fallbackHost,
                    ["seederCount"] = t.Seeders,
                    ["leecherCount"] = t.Leechers,
                    ["downloadCount"] = 0L,
                    ["hasAnnounced"] = true,
                    ["hasScraped"] = false,
                    ["lastAnnounceSucceeded"] = true,
                    ["lastAnnounceTime"] = 0L,
                    ["lastScrapeTime"] = 0L,
                    ["nextAnnounceTime"] = 0L,
                });
            }
        }

        var labelList = !string.IsNullOrWhiteSpace(t.Label)
            ? t.Label.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList()
            : new List<string>();

        if (labelList.Count == 0 && !string.IsNullOrWhiteSpace(t.Category))
        {
            labelList.Add(t.Category.Trim());
        }

        var fullDict = new Dictionary<string, object>
        {
            ["id"] = t.Id,
            ["name"] = t.Name ?? string.Empty,
            ["hashString"] = (t.InfoHash ?? string.Empty).ToLowerInvariant(),
            ["totalSize"] = totalSize,
            ["percentDone"] = t.Progress,
            ["status"] = MapToTransmissionStatus(t.Status, t.Progress),
            ["rateDownload"] = t.DownloadSpeed,
            ["rateUpload"] = t.UploadSpeed,
            ["uploadedEver"] = t.Uploaded,
            ["downloadedEver"] = downloaded,
            ["uploadRatio"] = t.Ratio,
            ["eta"] = CalculateEta(t),
            ["peersConnected"] = t.Leechers + t.Seeders,
            ["peersGettingFromUs"] = t.Leechers,
            ["peersSendingToUs"] = t.Seeders,
            ["peersTotal"] = t.Leechers + t.Seeders,
            ["seeders"] = t.Seeders,
            ["leechers"] = t.Leechers,
            ["downloadDir"] = RemapLocalToRemote(!string.IsNullOrWhiteSpace(t.SavePath) ? t.SavePath : (!string.IsNullOrWhiteSpace(t.SourcePath) ? t.SourcePath : (_configService?.WatchFolderPath ?? "/downloads"))),
            ["isFinished"] = t.Progress >= 1.0,
            ["isStalled"] = t.Status == TorrentStatus.Downloading && t.DownloadSpeed == 0,
            ["error"] = t.Status == TorrentStatus.Error ? 1 : 0,
            ["errorString"] = t.ErrorMessage ?? string.Empty,
            ["addedDate"] = new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds(),
            ["activityDate"] = new DateTimeOffset(t.LastActive ?? t.DateAdded).ToUnixTimeSeconds(),
            ["doneDate"] = t.Progress >= 1.0 ? new DateTimeOffset(t.LastActive ?? t.DateAdded).ToUnixTimeSeconds() : 0,
            ["bandwidthPriority"] = t.Priority,
            ["labels"] = labelList,
            ["trackers"] = trackers,
            ["files"] = files.Select(f => new Dictionary<string, object>
            {
                ["bytesCompleted"] = f.BytesCompleted > 0 ? f.BytesCompleted : (long)(f.Size * t.Progress),
                ["length"] = f.Size,
                ["name"] = f.Path ?? string.Empty,
            }).ToList(),
            ["fileStats"] = files.Select(f => new Dictionary<string, object>
            {
                ["bytesCompleted"] = f.BytesCompleted > 0 ? f.BytesCompleted : (long)(f.Size * t.Progress),
                ["wanted"] = f.Wanted,
                ["priority"] = f.Priority,
            }).ToList(),
            ["priorities"] = files.Select(f => f.Priority).ToList(),
            ["wanted"] = files.Select(f => f.Wanted ? 1 : 0).ToList(),
            ["trackerStats"] = trackerStats,
            ["pieceCount"] = t.PieceCount,
            ["pieceSize"] = t.PieceLength,
            ["sizeWhenDone"] = sizeWhenDone,
            ["leftUntilDone"] = leftUntilDone,
            ["recheckProgress"] = 1.0,
            ["queuePosition"] = t.SortOrder,
            ["secondsDownloading"] = 0,
            ["secondsSeeding"] = t.SeedingTime,
        };

        if (fields == null || fields.Count == 0)
        {
            return fullDict;
        }

        var result = new Dictionary<string, object>();
        foreach (var field in fields)
        {
            if (fullDict.TryGetValue(field, out var val))
            {
                result[field] = val;
            }
        }

        return result;
    }

    private static long CalculateEta(Torrent t)
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

        if (t.DownloadSpeed > 0 && t.TotalSize > 0)
        {
            var leftBytes = Math.Max(0L, t.TotalSize - t.Downloaded);
            return leftBytes / t.DownloadSpeed;
        }

        return 8640000;
    }

    private static int MapToTransmissionStatus(TorrentStatus status, double progress)
    {
        return status switch
        {
            TorrentStatus.Stopped => 0,
            TorrentStatus.Paused => 0,
            TorrentStatus.QueuedForChecking => 1,
            TorrentStatus.Checking => 2,
            TorrentStatus.Queued => progress >= 1.0 ? 5 : 3,
            TorrentStatus.Downloading => 4,
            TorrentStatus.Seeding => 6,
            TorrentStatus.Moving => 0,
            TorrentStatus.Error => 0,
            _ => 0,
        };
    }

    [NonAction]
    public List<int> ExtractIds(Dictionary<string, JsonElement> arguments, bool returnAllIfMissing = true)
    {
        var result = new List<int>();
        if (arguments == null)
        {
            return returnAllIfMissing ? _torrentService.GetAll().Select(t => t.Id).ToList() : result;
        }

        if (arguments.TryGetValue("ids", out var idsElem))
        {
            Dictionary<string, int> hashToId = null;

            if (idsElem.ValueKind == JsonValueKind.Number && idsElem.TryGetInt32(out var idNum))
            {
                result.Add(idNum);
            }
            else if (idsElem.ValueKind == JsonValueKind.String)
            {
                var hash = idsElem.GetString();
                if (!string.Equals(hash, "recently-active", StringComparison.OrdinalIgnoreCase))
                {
                    hashToId ??= _torrentService.GetAll()
                        .Where(t => !string.IsNullOrEmpty(t.InfoHash))
                        .GroupBy(t => t.InfoHash, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

                    if (hashToId.TryGetValue(hash, out var tId))
                    {
                        result.Add(tId);
                    }
                }
            }
            else if (idsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in idsElem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var arrId))
                    {
                        result.Add(arrId);
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        var arrHash = item.GetString();
                        if (!string.Equals(arrHash, "recently-active", StringComparison.OrdinalIgnoreCase))
                        {
                            hashToId ??= _torrentService.GetAll()
                                .Where(t => !string.IsNullOrEmpty(t.InfoHash))
                                .GroupBy(t => t.InfoHash, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

                            if (hashToId.TryGetValue(arrHash, out var tId))
                            {
                                result.Add(tId);
                            }
                        }
                    }
                }
            }
        }
        else if (returnAllIfMissing)
        {
            return _torrentService.GetAll().Select(t => t.Id).ToList();
        }

        return result;
    }

    private static bool SafeGetBoolean(JsonElement elem, bool defaultValue = false)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => elem.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => bool.TryParse(elem.GetString(), out var b) ? b : defaultValue,
            _ => defaultValue,
        };
    }
}
