using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
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
public class TransmissionRpcController : ControllerBase
{
    private const string SessionHeaderName = "X-Transmission-Session-Id";
    private static readonly object _removedLock = new();
    private static readonly List<(int Id, DateTime RemovedAt)> _recentlyRemovedList = new();
    private static readonly DateTime _serviceStartTime = DateTime.UtcNow;

    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ITorrentImportService _torrentImportService;
    private readonly ITrackerEntryService _trackerEntryService;
    private readonly IConfigService _configService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public static void RecordRemovedId(int id)
    {
        lock (_removedLock)
        {
            _recentlyRemovedList.Add((id, DateTime.UtcNow));
            _recentlyRemovedList.RemoveAll(x => (DateTime.UtcNow - x.RemovedAt).TotalMinutes > 10);
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
        HttpClient httpClient = null)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _torrentImportService = torrentImportService;
        _trackerEntryService = trackerEntryService;
        _configService = configService;
        _configFileProvider = configFileProvider;
        _httpClient = httpClient ?? new HttpClient();
        _logger = LogManager.GetCurrentClassLogger();
    }

    [HttpGet]
    public IActionResult HandleGet()
    {
        if (!RpcAuthenticationHelper.IsAuthenticated(HttpContext, _configFileProvider))
        {
            Response.Headers["WWW-Authenticate"] = "Basic realm=\"Transmission\"";
            return Unauthorized();
        }

        if (!Request.Headers.TryGetValue(SessionHeaderName, out var sessionVal) || string.IsNullOrEmpty(sessionVal))
        {
            var newSessionId = Guid.NewGuid().ToString("N");
            Response.Headers[SessionHeaderName] = newSessionId;
            return StatusCode(409, "Conflict: Session ID generated.");
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

        if (!Request.Headers.TryGetValue(SessionHeaderName, out var sessionVal) || string.IsNullOrEmpty(sessionVal))
        {
            var newSessionId = Guid.NewGuid().ToString("N");
            Response.Headers[SessionHeaderName] = newSessionId;
            return StatusCode(409, "Conflict: Session ID generated.");
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
                { "download-dir", _configService?.WatchFolderPath ?? "/downloads" },
                { "incomplete-dir", "/downloads/incomplete" },
                { "incomplete-dir-enabled", false },
                { "speed-limit-down", _configService?.MaxDownloadSpeedKbps ?? 1250 },
                { "speed-limit-up", _configService?.MaxUploadSpeedKbps ?? 625 },
                { "speed-limit-down-enabled", (_configService?.MaxDownloadSpeedKbps ?? 0) > 0 },
                { "speed-limit-up-enabled", (_configService?.MaxUploadSpeedKbps ?? 0) > 0 },
                { "seedRatioLimit", _configService?.GlobalSeedRatioLimit ?? 0.0 },
                { "seedRatioLimited", (_configService?.GlobalSeedRatioLimit ?? 0.0) > 0 },
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
                updates["WatchFolderPath"] = dlDir.GetString();
            }

            if (request.Arguments.TryGetValue("speed-limit-down", out var dlLimit) && dlLimit.ValueKind == JsonValueKind.Number)
            {
                updates["MaxDownloadSpeedKbps"] = dlLimit.GetInt32();
            }

            if (request.Arguments.TryGetValue("speed-limit-up", out var upLimit) && upLimit.ValueKind == JsonValueKind.Number)
            {
                updates["MaxUploadSpeedKbps"] = upLimit.GetInt32();
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
        var targetIds = ExtractIds(request.Arguments);

        if (targetIds.Count > 0)
        {
            var targetIdSet = targetIds.ToHashSet();
            torrents = torrents.Where(t => targetIdSet.Contains(t.Id));
        }
        else if (isRecentlyActive)
        {
            torrents = torrents.Where(t => t.Status == TorrentStatus.Downloading ||
                                           t.Status == TorrentStatus.Seeding ||
                                           t.DownloadSpeed > 0 ||
                                           t.UploadSpeed > 0);
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
        var setIds = ExtractIds(request.Arguments);
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
                        t.SourcePath = targetLocation;
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

                _torrentService.Update(t);
            }
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentSetLocation(TransmissionRpcRequest request, object tag)
    {
        var locIds = ExtractIds(request.Arguments, false);
        var newLocation = request.Arguments != null && request.Arguments.TryGetValue("location", out var locElem)
            ? locElem.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(newLocation))
        {
            foreach (var id in locIds)
            {
                var t = _torrentService.Get(id);
                if (t != null)
                {
                    t.SourcePath = newLocation;
                    _torrentService.Update(t);
                }
            }
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

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
                    using var reqMsg = new HttpRequestMessage(HttpMethod.Get, filename);
                    if (!string.IsNullOrWhiteSpace(cookies))
                    {
                        reqMsg.Headers.TryAddWithoutValidation("Cookie", cookies);
                    }

                    using var resp = await _httpClient.SendAsync(reqMsg);
                    resp.EnsureSuccessStatusCode();

                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    using var ms = new MemoryStream(bytes);
                    added = _torrentImportService.ImportFromFile(ms, "downloaded.torrent");
                }
            }

            if (added != null)
            {
                var needsUpdate = false;
                if (!string.IsNullOrWhiteSpace(downloadDir))
                {
                    added.SourcePath = downloadDir;
                    needsUpdate = true;
                }

                if (labels != null && labels.Count > 0)
                {
                    added.Label = string.Join(",", labels);
                    needsUpdate = true;
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
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            var existing = _torrentService.GetAll().FirstOrDefault();
            return Ok(new TransmissionRpcResponse
            {
                Result = "success",
                Arguments = new Dictionary<string, object>
                {
                    ["torrent-duplicate"] = new Dictionary<string, object>
                    {
                        ["id"] = existing?.Id ?? 1,
                        ["name"] = existing?.Name ?? "Torrent",
                        ["hashString"] = (existing?.InfoHash ?? string.Empty).ToLowerInvariant(),
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

    private IActionResult HandleFreeSpace(TransmissionRpcRequest request, object tag)
    {
        var targetPath = request.Arguments != null && request.Arguments.TryGetValue("path", out var p) ? p.GetString() : (_configService?.WatchFolderPath ?? "/downloads");
        var freeBytes = 100L * 1024 * 1024 * 1024;

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

    private IActionResult HandleQueueMove(TransmissionRpcRequest request, object tag, string position)
    {
        var ids = ExtractIds(request.Arguments);
        foreach (var id in ids)
        {
            _torrentService.MoveQueue(id, position);
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentStart(TransmissionRpcRequest request, object tag)
    {
        var ids = ExtractIds(request.Arguments);
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
        var ids = ExtractIds(request.Arguments);
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
        var ids = ExtractIds(request.Arguments);
        foreach (var id in ids)
        {
            _torrentService.Recheck(id);
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentReannounce(TransmissionRpcRequest request, object tag)
    {
        var ids = ExtractIds(request.Arguments);
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
        var deleteData = request.Arguments != null && request.Arguments.TryGetValue("delete-local-data", out var d) && SafeGetBoolean(d);
        var ids = ExtractIds(request.Arguments);

        foreach (var id in ids)
        {
            RecordRemovedId(id);
            _torrentService.Delete(id, deleteData);
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
    }

    private IActionResult HandleTorrentRenamePath(TransmissionRpcRequest request, object tag)
    {
        var ids = ExtractIds(request.Arguments);
        var name = request.Arguments != null && request.Arguments.TryGetValue("name", out var n) ? n.GetString() : null;

        if (ids.Count > 0 && !string.IsNullOrWhiteSpace(name))
        {
            var t = _torrentService.Get(ids[0]);
            if (t != null)
            {
                t.Name = name;
                _torrentService.Update(t);
            }
        }

        return Ok(new TransmissionRpcResponse { Result = "success", Tag = tag });
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

    private Dictionary<string, object> MapTorrentToTransmission(Torrent t, HashSet<string> fields)
    {
        var totalSize = t.TotalSize;
        var downloaded = t.Downloaded;
        var leftUntilDone = Math.Max(0, totalSize - downloaded);
        var files = _torrentFileService.GetByTorrentId(t.Id);

        var trkList = _trackerEntryService.GetByTorrentId(t.Id);
        var trackers = trkList.Select((tr, idx) => new Dictionary<string, object>
        {
            ["id"] = tr.Id,
            ["announce"] = tr.Url ?? string.Empty,
            ["scrape"] = tr.Url ?? string.Empty,
            ["tier"] = tr.Tier,
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
        }

        var labelList = !string.IsNullOrWhiteSpace(t.Label)
            ? t.Label.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList()
            : new List<string>();

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
            ["downloadDir"] = !string.IsNullOrWhiteSpace(t.SourcePath) ? t.SourcePath : (_configService?.WatchFolderPath ?? "/downloads"),
            ["isFinished"] = t.Progress >= 1.0,
            ["isStalled"] = t.Status == TorrentStatus.Downloading && t.DownloadSpeed == 0,
            ["error"] = t.Status == TorrentStatus.Error ? 1 : 0,
            ["errorString"] = string.Empty,
            ["addedDate"] = new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds(),
            ["activityDate"] = new DateTimeOffset(t.LastActive ?? t.DateAdded).ToUnixTimeSeconds(),
            ["doneDate"] = t.Progress >= 1.0 ? new DateTimeOffset(t.LastActive ?? t.DateAdded).ToUnixTimeSeconds() : 0,
            ["bandwidthPriority"] = t.Priority,
            ["labels"] = labelList,
            ["trackers"] = trackers,
            ["files"] = files.Select(f => new Dictionary<string, object>
            {
                ["bytesCompleted"] = (long)(f.Size * t.Progress),
                ["length"] = f.Size,
                ["name"] = f.Path ?? string.Empty,
            }).ToList(),
            ["pieceCount"] = t.PieceCount,
            ["pieceSize"] = t.PieceLength,
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
            TorrentStatus.Queued => progress >= 1.0 ? 5 : 3,
            TorrentStatus.Downloading => 4,
            TorrentStatus.Seeding => 6,
            TorrentStatus.Error => 0,
            _ => 0,
        };
    }

    private List<int> ExtractIds(Dictionary<string, JsonElement> arguments, bool returnAllIfMissing = true)
    {
        var result = new List<int>();
        if (arguments == null)
        {
            return returnAllIfMissing ? _torrentService.GetAll().Select(t => t.Id).ToList() : result;
        }

        if (arguments.TryGetValue("ids", out var idsElem))
        {
            if (idsElem.ValueKind == JsonValueKind.Number && idsElem.TryGetInt32(out var idNum))
            {
                result.Add(idNum);
            }
            else if (idsElem.ValueKind == JsonValueKind.String)
            {
                var hash = idsElem.GetString();
                if (!string.Equals(hash, "recently-active", StringComparison.OrdinalIgnoreCase))
                {
                    var t = _torrentService.GetAll().FirstOrDefault(x => string.Equals(x.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
                    if (t != null)
                    {
                        result.Add(t.Id);
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
                            var t = _torrentService.GetAll().FirstOrDefault(x => string.Equals(x.InfoHash, arrHash, StringComparison.OrdinalIgnoreCase));
                            if (t != null)
                            {
                                result.Add(t.Id);
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
