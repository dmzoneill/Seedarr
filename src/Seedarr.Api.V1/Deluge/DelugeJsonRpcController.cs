using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;
using Seedarr.Http.Security;

namespace Seedarr.Api.V1.Deluge;

[AllowAnonymous]
[ApiController]
[Route("json")]
public class DelugeJsonRpcController : ControllerBase
{
    private static readonly RpcSessionStore _authenticatedSessions = new();

    private static readonly JsonSerializerOptions _delugeJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITorrentService _torrentService;
    private readonly ITorrentFileService _torrentFileService;
    private readonly ITorrentFileParser _torrentFileParser;
    private readonly ITorrentImportService _torrentImportService;
    private readonly IConfigService _configService;
    private readonly ITagService _tagService;
    private readonly IConfigFileProvider _configFileProvider;
    private readonly HttpClient _httpClient;
    private readonly Logger _logger;

    public DelugeJsonRpcController(
        ITorrentService torrentService,
        ITorrentFileService torrentFileService,
        ITorrentFileParser torrentFileParser,
        ITorrentImportService torrentImportService,
        IConfigService configService,
        ITagService tagService = null,
        IConfigFileProvider configFileProvider = null,
        HttpClient httpClient = null)
    {
        _torrentService = torrentService;
        _torrentFileService = torrentFileService;
        _torrentFileParser = torrentFileParser;
        _torrentImportService = torrentImportService;
        _configService = configService;
        _tagService = tagService;
        _configFileProvider = configFileProvider;
        _httpClient = httpClient ?? new HttpClient();
        _logger = LogManager.GetCurrentClassLogger();
    }

    private bool IsDelugeAuthenticated()
    {
        if (_configFileProvider != null && !_configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (RpcAuthenticationHelper.IsAuthenticated(HttpContext, _configFileProvider))
        {
            return true;
        }

        if (HttpContext?.Items.TryGetValue("deluge-session", out var itemSid) == true && itemSid is string s && _authenticatedSessions.IsValid(s))
        {
            return true;
        }

        if (Request?.Cookies.TryGetValue("_session_id", out var sid1) == true && !string.IsNullOrWhiteSpace(sid1))
        {
            if (_authenticatedSessions.IsValid(sid1))
            {
                return true;
            }
        }

        if (Request?.Cookies.TryGetValue("deluge-session", out var sid2) == true && !string.IsNullOrWhiteSpace(sid2))
        {
            if (_authenticatedSessions.IsValid(sid2))
            {
                return true;
            }
        }

        return false;
    }

    private IActionResult DelugeResult(object value)
    {
        return new JsonResult(value, _delugeJsonOptions);
    }

    [HttpPost]
    public async Task<IActionResult> HandleRpc([FromBody] JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            var responses = new List<object>();
            foreach (var item in root.EnumerateArray())
            {
                var singleResult = await ProcessSingleRpcAsync(item);
                if (singleResult is JsonResult jsonResult)
                {
                    responses.Add(jsonResult.Value);
                }
                else if (singleResult is ObjectResult objectResult)
                {
                    responses.Add(objectResult.Value);
                }
                else
                {
                    responses.Add(new { result = (object)null, error = "Unknown RPC result", id = (object)null });
                }
            }

            return DelugeResult(responses);
        }

        return await ProcessSingleRpcAsync(root);
    }

    private async Task<IActionResult> ProcessSingleRpcAsync(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return DelugeResult(new { result = (object)null, error = new { message = "Invalid JSON-RPC format", code = 1 }, id = (object)null });
        }

        var methodElem = root.TryGetProperty("method", out var m) ? m : default;
        var method = methodElem.ValueKind == JsonValueKind.String ? methodElem.GetString() : string.Empty;

        object id = null;
        if (root.TryGetProperty("id", out var idElem))
        {
            if (idElem.ValueKind == JsonValueKind.Number && idElem.TryGetInt64(out var numId))
            {
                id = numId;
            }
            else if (idElem.ValueKind == JsonValueKind.String)
            {
                id = idElem.GetString();
            }
        }

        var paramsElem = root.TryGetProperty("params", out var p) ? p : default;

        try
        {
            var lowerMethod = method.ToLowerInvariant();

            if (lowerMethod == "auth.login")
            {
                return HandleAuthLogin(paramsElem, id);
            }

            if (lowerMethod == "auth.check_session")
            {
                return HandleAuthCheckSession(id);
            }

            if (lowerMethod == "auth.delete_session")
            {
                return HandleAuthDeleteSession(id);
            }

            if (!IsDelugeAuthenticated())
            {
                return DelugeResult(new { result = (object)null, error = new { message = "Not authenticated", code = 1 }, id });
            }

            if (lowerMethod.StartsWith("core."))
            {
                return await DispatchCoreRpcAsync(lowerMethod, paramsElem, id);
            }

            if (lowerMethod.StartsWith("web."))
            {
                return await DispatchWebRpcAsync(lowerMethod, paramsElem, id);
            }

            if (lowerMethod.StartsWith("daemon.") || lowerMethod.StartsWith("system."))
            {
                return DispatchDaemonRpc(lowerMethod, paramsElem, id);
            }

            if (lowerMethod.StartsWith("label."))
            {
                return await DispatchLabelRpcAsync(lowerMethod, paramsElem, id);
            }

            return HandleUnknownMethod(method, id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling Deluge RPC method: {0}", method);
            return DelugeResult(new { result = (object)null, error = ex.Message, id });
        }
    }

    private async Task<IActionResult> DispatchCoreRpcAsync(string method, JsonElement args, object id)
    {
        return method switch
        {
            "core.get_version" => HandleGetVersion(id),
            "core.get_enabled_plugins" or "core.get_available_plugins" => HandleGetPlugins(id),
            "core.enable_plugin" or "core.disable_plugin" => HandleTogglePlugin(id),
            "core.get_config" => HandleCoreGetConfig(id),
            "core.get_config_values" => HandleCoreGetConfigValues(args, id),
            "core.get_config_value" => HandleCoreGetConfigValue(args, id),
            "core.set_config" or "core.set_config_values" => HandleCoreSetConfig(args, id),
            "core.get_session_status" => HandleCoreGetSessionStatus(id),
            "core.get_free_space" or "core.get_path_free_space" or "core.get_free_space_bytes" => HandleCoreGetFreeSpace(args, id),
            "core.get_torrents_status" => HandleGetTorrentsStatus(args, id, isWeb: false),
            "core.get_torrent_status" => HandleGetTorrentStatus(args, id),
            "core.add_torrent_file" or "core.add_torrent_file_async" => await HandleCoreAddTorrentFileAsync(args, id),
            "core.add_torrent_magnet" => await HandleCoreAddTorrentMagnetAsync(args, id),
            "core.add_torrent_url" => await HandleCoreAddTorrentUrlAsync(args, id),
            "core.pause_torrent" or "core.pause_torrents" or "core.pause_all_torrents" => HandleCorePauseTorrents(method, args, id),
            "core.resume_torrent" or "core.resume_torrents" or "core.resume_all_torrents" => HandleCoreResumeTorrents(method, args, id),
            "core.remove_torrent" or "core.remove_torrents" => HandleCoreRemoveTorrents(method, args, id),
            "core.force_recheck" => HandleCoreForceRecheck(args, id),
            "core.force_reannounce" or "core.reannounce" => HandleCoreForceReannounce(args, id),
            "core.move_storage" => HandleCoreMoveStorage(args, id),
            "core.set_torrent_options" => HandleCoreSetTorrentOptions(args, id),
            "core.set_torrent_file_priorities" => HandleCoreSetTorrentFilePriorities(args, id),
            "core.rename_files" => HandleCoreRenameFiles(args, id),
            "core.queue_top" or "core.queue_up" or "core.queue_down" or "core.queue_bottom" => HandleCoreQueue(method, args, id),
            "core.get_filter_tree" => HandleGetFilterTree(id),
            _ => HandleUnknownMethod(method, id),
        };
    }

    private async Task<IActionResult> DispatchWebRpcAsync(string method, JsonElement args, object id)
    {
        return method switch
        {
            "web.connected" or "web.connect" => HandleWebConnected(id),
            "web.get_version" => HandleGetVersion(id),
            "web.get_plugins" or "web.get_installed_plugins" => HandleGetPlugins(id),
            "web.get_hosts" => HandleWebGetHosts(id),
            "web.get_host_status" => HandleWebGetHostStatus(id),
            "web.update_ui" => HandleWebUpdateUi(args, id),
            "web.get_config" => HandleCoreGetConfig(id),
            "web.set_config" => HandleCoreSetConfig(args, id),
            "web.get_torrents_status" => HandleGetTorrentsStatus(args, id, isWeb: true),
            "web.get_torrent_status" => HandleGetTorrentStatus(args, id),
            "web.upload_torrent" => await HandleWebUploadTorrentAsync(args, id),
            "web.get_torrent_info" => await HandleWebGetTorrentInfoAsync(args, id),
            "web.add_torrents" => await HandleWebAddTorrentsAsync(args, id),
            "web.disconnect" => HandleWebDisconnect(id),
            "web.get_filter_tree" => HandleGetFilterTree(id),
            _ => HandleUnknownMethod(method, id),
        };
    }

    private IActionResult DispatchDaemonRpc(string method, JsonElement args, object id)
    {
        return method switch
        {
            "system.listmethods" or "system.list_methods" or "daemon.get_method_list" or "system.get_methods" => HandleSystemListMethods(id),
            "daemon.get_version" or "daemon.info" => HandleGetVersion(id),
            _ => HandleUnknownMethod(method, id),
        };
    }

    private async Task<IActionResult> DispatchLabelRpcAsync(string method, JsonElement args, object id)
    {
        return method switch
        {
            "label.get_labels" => HandleLabelGetLabels(id),
            "label.get_torrents" => HandleLabelGetTorrents(args, id),
            "label.add" or "label.add_label" => HandleLabelAdd(args, id),
            "label.remove" => HandleLabelRemove(args, id),
            "label.get_options" => HandleLabelGetOptions(args, id),
            "label.set_options" => HandleLabelSetOptions(args, id),
            "label.set_torrent" => await HandleLabelSetTorrentAsync(args, id),
            _ => HandleUnknownMethod(method, id),
        };
    }

    private IActionResult HandleAuthLogin(JsonElement paramsElem, object id)
    {
        var providedPassword = string.Empty;
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 &&
            paramsElem[0].ValueKind == JsonValueKind.String)
        {
            providedPassword = paramsElem[0].GetString();
        }

        var loginSuccess = false;
        if (_configFileProvider == null || !_configFileProvider.AuthenticationEnabled)
        {
            loginSuccess = true;
        }
        else if (!string.IsNullOrWhiteSpace(_configFileProvider.ApiKey) &&
                 RpcAuthenticationHelper.FixedTimeEquals(providedPassword, _configFileProvider.ApiKey))
        {
            loginSuccess = true;
        }

        if (loginSuccess)
        {
            var sid = Guid.NewGuid().ToString("N");
            _authenticatedSessions.SetSession(sid, DateTime.UtcNow.AddDays(7));
            if (HttpContext != null)
            {
                HttpContext.Items["deluge-session"] = sid;
            }

            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
            };
            Response?.Cookies.Append("_session_id", sid, cookieOptions);
            Response?.Cookies.Append("deluge-session", sid, cookieOptions);

            return DelugeResult(new { result = true, error = (object)null, id });
        }

        return DelugeResult(new { result = false, error = (object)null, id });
    }

    private IActionResult HandleAuthCheckSession(object id)
    {
        var isAuth = IsDelugeAuthenticated();
        return DelugeResult(new { result = isAuth, error = (object)null, id });
    }

    private IActionResult HandleAuthDeleteSession(object id)
    {
        if (HttpContext?.Items.TryGetValue("deluge-session", out var itemSid) == true && itemSid is string s)
        {
            _authenticatedSessions.RemoveSession(s);
            HttpContext.Items.Remove("deluge-session");
        }

        if (Request?.Cookies.TryGetValue("_session_id", out var sid1) == true && !string.IsNullOrWhiteSpace(sid1))
        {
            _authenticatedSessions.RemoveSession(sid1);
        }

        if (Request?.Cookies.TryGetValue("deluge-session", out var sid2) == true && !string.IsNullOrWhiteSpace(sid2))
        {
            _authenticatedSessions.RemoveSession(sid2);
        }

        Response?.Cookies.Delete("_session_id");
        Response?.Cookies.Delete("deluge-session");

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleWebConnected(object id)
    {
        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleWebDisconnect(object id)
    {
        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleSystemListMethods(object id)
    {
        return DelugeResult(new
        {
            result = new[]
            {
                "auth.login",
                "auth.check_session",
                "auth.delete_session",
                "web.connected",
                "web.connect",
                "web.get_hosts",
                "web.get_host_status",
                "web.update_ui",
                "web.get_plugins",
                "web.get_installed_plugins",
                "web.get_config",
                "web.set_config",
                "web.get_torrents_status",
                "web.upload_torrent",
                "web.get_torrent_info",
                "web.add_torrents",
                "core.get_version",
                "daemon.get_version",
                "daemon.info",
                "system.listMethods",
                "system.list_methods",
                "system.get_methods",
                "core.get_config",
                "core.get_config_values",
                "core.get_config_value",
                "core.set_config",
                "core.set_config_values",
                "core.get_session_status",
                "core.get_free_space",
                "core.get_path_free_space",
                "core.get_free_space_bytes",
                "core.get_torrents_status",
                "core.get_torrent_status",
                "core.add_torrent_file",
                "core.add_torrent_magnet",
                "core.add_torrent_url",
                "core.pause_torrent",
                "core.pause_torrents",
                "core.pause_all_torrents",
                "core.resume_torrent",
                "core.resume_torrents",
                "core.resume_all_torrents",
                "core.remove_torrent",
                "core.remove_torrents",
                "core.force_recheck",
                "core.force_reannounce",
                "core.set_torrent_options",
                "core.set_torrent_file_priorities",
                "core.rename_files",
                "core.move_storage",
                "core.queue_top",
                "core.queue_up",
                "core.queue_down",
                "core.queue_bottom",
                "core.get_filter_tree",
                "web.get_filter_tree",
                "core.get_enabled_plugins",
                "core.get_available_plugins",
                "core.enable_plugin",
                "core.disable_plugin",
                "label.get_labels",
                "label.get_torrents",
                "label.set_torrent",
                "label.add",
                "label.add_label",
                "label.remove",
                "label.get_options",
                "label.set_options",
            },
            error = (object)null,
            id,
        });
    }

    private IActionResult HandleGetVersion(object id)
    {
        return DelugeResult(new { result = "2.1.1", error = (object)null, id });
    }

    private IActionResult HandleLabelGetLabels(object id)
    {
        var torrents = _torrentService.GetAll();
        var labels = torrents
            .Select(t => t.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return DelugeResult(new { result = labels, error = (object)null, id });
    }

    private IActionResult HandleLabelGetTorrents(JsonElement paramsElem, object id)
    {
        var targetLabel = GetFirstStringParam(paramsElem);
        var allTorrents = _torrentService.GetAll();
        IEnumerable<Torrent> matching;

        if (targetLabel == null || string.Equals(targetLabel, "All", StringComparison.OrdinalIgnoreCase))
        {
            matching = allTorrents;
        }
        else if (string.IsNullOrEmpty(targetLabel) || string.Equals(targetLabel, "no_label", StringComparison.OrdinalIgnoreCase) || string.Equals(targetLabel, "None", StringComparison.OrdinalIgnoreCase))
        {
            matching = allTorrents.Where(t => string.IsNullOrWhiteSpace(t.Label));
        }
        else
        {
            matching = allTorrents.Where(t => string.Equals(t.Label, targetLabel, StringComparison.OrdinalIgnoreCase));
        }

        var torrentHashes = matching.Select(t => (t.InfoHash ?? string.Empty).ToLowerInvariant()).ToArray();
        return DelugeResult(new { result = torrentHashes, error = (object)null, id });
    }

    private IActionResult HandleLabelAdd(JsonElement paramsElem, object id)
    {
        var newLabel = GetFirstStringParam(paramsElem);
        if (!string.IsNullOrWhiteSpace(newLabel) && _tagService != null)
        {
            var existing = _tagService.GetAll().FirstOrDefault(t => string.Equals(t.Label, newLabel, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                _tagService.Add(new Tag { Label = newLabel });
            }
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleLabelRemove(JsonElement paramsElem, object id)
    {
        var labelToRemove = GetFirstStringParam(paramsElem);
        if (!string.IsNullOrEmpty(labelToRemove) && _tagService != null)
        {
            var tag = _tagService.GetAll().FirstOrDefault(t => string.Equals(t.Label, labelToRemove, StringComparison.OrdinalIgnoreCase));
            if (tag != null)
            {
                _tagService.Delete(tag.Id);
            }
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleLabelGetOptions(JsonElement paramsElem, object id)
    {
        var labelOpts = new Dictionary<string, object>
        {
            ["apply_max"] = false,
            ["max_download_speed"] = -1,
            ["max_upload_speed"] = -1,
            ["apply_queue"] = false,
            ["stop_at_ratio"] = false,
            ["stop_ratio"] = 2.0,
            ["remove_at_ratio"] = false,
            ["apply_move_completed"] = false,
            ["move_completed_path"] = string.Empty,
        };
        return DelugeResult(new { result = labelOpts, error = (object)null, id });
    }

    private IActionResult HandleLabelSetOptions(JsonElement paramsElem, object id)
    {
        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private async Task<IActionResult> HandleLabelSetTorrentAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
        {
            var torrentHash = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
            var labelName = paramsElem[1].ValueKind == JsonValueKind.String ? paramsElem[1].GetString() : null;
            if (!string.IsNullOrEmpty(torrentHash))
            {
                var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, torrentHash, StringComparison.OrdinalIgnoreCase));
                if (torrent != null)
                {
                    torrent.Label = labelName;
                    _torrentService.Update(torrent);
                }
            }
        }

        await Task.CompletedTask;
        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleGetPlugins(object id)
    {
        return DelugeResult(new { result = new[] { "Label", "Extractor", "Execute", "AutoAdd", "Blocklist", "Scheduler", "Stats" }, error = (object)null, id });
    }

    private IActionResult HandleTogglePlugin(object id)
    {
        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleWebGetHosts(object id)
    {
        return DelugeResult(new { result = new object[] { new object[] { "1", "127.0.0.1", 58846, "Connected" } }, error = (object)null, id });
    }

    private IActionResult HandleWebGetHostStatus(object id)
    {
        return DelugeResult(new { result = new object[] { "1", "Connected", "2.1.1" }, error = (object)null, id });
    }

    private IActionResult HandleWebUpdateUi(JsonElement paramsElem, object id)
    {
        var allTorrents = _torrentService.GetAll();
        var (uiFilterObj, uiKeys) = ParseStatusParams(paramsElem, isWebUpdateUi: true);
        var filteredTorrents = FilterTorrents(allTorrents, uiFilterObj);

        var torrentDict = new Dictionary<string, Dictionary<string, object>>();
        foreach (var t in filteredTorrents)
        {
            torrentDict[(t.InfoHash ?? string.Empty).ToLowerInvariant()] = MapTorrentToDelugeStatus(t, uiKeys);
        }

        return DelugeResult(new
        {
            result = new
            {
                connected = true,
                torrents = torrentDict,
                filters = BuildFilterTree(allTorrents),
                stats = new
                {
                    max_download = _configService?.MaxDownloadSpeedKbps ?? 1250,
                    max_upload = _configService?.MaxUploadSpeedKbps ?? 625,
                    num_connections = allTorrents.Sum(t => t.Leechers + t.Seeders),
                    upload_rate = allTorrents.Sum(t => t.UploadSpeed),
                    download_rate = allTorrents.Sum(t => t.DownloadSpeed),
                    free_space = 100L * 1024 * 1024 * 1024,
                },
            },
            error = (object)null,
            id,
        });
    }

    private IActionResult HandleCoreGetConfig(object id)
    {
        return DelugeResult(new
        {
            result = GetDelugeConfigDictionary(),
            error = (object)null,
            id,
        });
    }

    private IActionResult HandleCoreGetConfigValues(JsonElement paramsElem, object id)
    {
        var fullConfig = GetDelugeConfigDictionary();
        var requestedConfig = new Dictionary<string, object>();
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 && paramsElem[0].ValueKind == JsonValueKind.Array)
        {
            foreach (var keyElem in paramsElem[0].EnumerateArray())
            {
                if (keyElem.ValueKind == JsonValueKind.String)
                {
                    var k = keyElem.GetString();
                    if (!string.IsNullOrEmpty(k))
                    {
                        requestedConfig[k] = fullConfig.TryGetValue(k, out var val) ? val : null;
                    }
                }
            }
        }

        return DelugeResult(new { result = requestedConfig, error = (object)null, id });
    }

    private IActionResult HandleCoreGetConfigValue(JsonElement paramsElem, object id)
    {
        var singleCfgKey = GetFirstStringParam(paramsElem);
        var singleFullConfig = GetDelugeConfigDictionary();
        singleFullConfig.TryGetValue(singleCfgKey ?? string.Empty, out var foundVal);
        return DelugeResult(new { result = foundVal, error = (object)null, id });
    }

    private IActionResult HandleCoreSetConfig(JsonElement paramsElem, object id)
    {
        if (_configService != null)
        {
            var cfgUpdates = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            JsonElement cfgElem = default;

            if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0)
            {
                cfgElem = paramsElem[0];
            }
            else if (paramsElem.ValueKind == JsonValueKind.Object)
            {
                cfgElem = paramsElem;
            }

            if (cfgElem.ValueKind == JsonValueKind.Object)
            {
                if (cfgElem.TryGetProperty("max_download_speed", out var dlProp) && dlProp.ValueKind == JsonValueKind.Number && dlProp.TryGetDouble(out var dlVal))
                {
                    cfgUpdates["MaxDownloadSpeedKbps"] = (int)Math.Round(dlVal);
                }

                if (cfgElem.TryGetProperty("max_upload_speed", out var ulProp) && ulProp.ValueKind == JsonValueKind.Number && ulProp.TryGetDouble(out var ulVal))
                {
                    cfgUpdates["MaxUploadSpeedKbps"] = (int)Math.Round(ulVal);
                }

                if (cfgElem.TryGetProperty("download_location", out var dlLocProp) && dlLocProp.ValueKind == JsonValueKind.String)
                {
                    cfgUpdates["WatchFolderPath"] = dlLocProp.GetString();
                }

                if (cfgElem.TryGetProperty("max_connections_global", out var mcgProp) && mcgProp.ValueKind == JsonValueKind.Number && mcgProp.TryGetInt32(out var mcgVal))
                {
                    cfgUpdates["MaxGlobalConnections"] = mcgVal;
                }

                if (cfgElem.TryGetProperty("max_connections_per_torrent", out var mcptProp) && mcptProp.ValueKind == JsonValueKind.Number && mcptProp.TryGetInt32(out var mcptVal))
                {
                    cfgUpdates["MaxPerTorrentConnections"] = mcptVal;
                }

                if (cfgUpdates.Count > 0)
                {
                    _configService.SaveConfigDictionary(cfgUpdates);
                }
            }
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreGetSessionStatus(object id)
    {
        var allTorrents = _torrentService.GetAll();
        var sessionStatus = new Dictionary<string, object>
        {
            ["upload_rate"] = (double)allTorrents.Sum(t => t.UploadSpeed),
            ["download_rate"] = (double)allTorrents.Sum(t => t.DownloadSpeed),
            ["num_peers"] = allTorrents.Sum(t => t.Leechers + t.Seeders),
            ["payload_upload_rate"] = (double)allTorrents.Sum(t => t.UploadSpeed),
            ["payload_download_rate"] = (double)allTorrents.Sum(t => t.DownloadSpeed),
            ["total_download"] = allTorrents.Sum(t => t.Downloaded),
            ["total_upload"] = allTorrents.Sum(t => t.Uploaded),
            ["total_payload_download"] = allTorrents.Sum(t => t.Downloaded),
            ["total_payload_upload"] = allTorrents.Sum(t => t.Uploaded),
            ["dht_nodes"] = 0,
            ["has_incoming_connections"] = true,
        };

        return DelugeResult(new { result = sessionStatus, error = (object)null, id });
    }

    private IActionResult HandleCoreGetFreeSpace(JsonElement paramsElem, object id)
    {
        var freeBytes = 100L * 1024 * 1024 * 1024;
        return DelugeResult(new { result = freeBytes, error = (object)null, id });
    }

    private IActionResult HandleGetTorrentsStatus(JsonElement paramsElem, object id, bool isWeb)
    {
        var allTorrents = _torrentService.GetAll();
        var (filterObj, keys) = ParseStatusParams(paramsElem, isWebUpdateUi: false);
        var filteredTorrents = FilterTorrents(allTorrents, filterObj);

        var result = new Dictionary<string, Dictionary<string, object>>();
        foreach (var t in filteredTorrents)
        {
            result[(t.InfoHash ?? string.Empty).ToLowerInvariant()] = MapTorrentToDelugeStatus(t, keys);
        }

        return DelugeResult(new { result, error = (object)null, id });
    }

    private IActionResult HandleGetTorrentStatus(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0)
        {
            var hash = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
            if (!string.IsNullOrEmpty(hash))
            {
                var torrent = _torrentService.GetAll().FirstOrDefault(t => string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
                if (torrent != null)
                {
                    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (paramsElem.GetArrayLength() > 1 && paramsElem[1].ValueKind == JsonValueKind.Array)
                    {
                        foreach (var k in paramsElem[1].EnumerateArray())
                        {
                            if (k.ValueKind == JsonValueKind.String)
                            {
                                keys.Add(k.GetString());
                            }
                        }
                    }

                    return DelugeResult(new { result = MapTorrentToDelugeStatus(torrent, keys), error = (object)null, id });
                }
            }
        }

        return DelugeResult(new { result = (object)null, error = "Torrent not found", id });
    }

    private async Task<IActionResult> HandleCoreAddTorrentFileAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
        {
            var fileName = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : "file.torrent";
            var fileDumpBase64 = paramsElem[1].ValueKind == JsonValueKind.String ? paramsElem[1].GetString() : null;
            var optionsElem = paramsElem.GetArrayLength() >= 3 ? paramsElem[2] : default;

            if (!string.IsNullOrWhiteSpace(fileDumpBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(fileDumpBase64);
                    using var ms = new MemoryStream(bytes);
                    var added = _torrentImportService.ImportFromFile(ms, fileName);

                    ApplyDelugeOptions(added, optionsElem);
                    return DelugeResult(new { result = (added.InfoHash ?? string.Empty).ToLowerInvariant(), error = (object)null, id });
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to add torrent file in Deluge RPC");
                    return DelugeResult(new { result = (object)null, error = ex.Message, id });
                }
            }
        }

        await Task.CompletedTask;
        return DelugeResult(new { result = (object)null, error = "Invalid arguments for add_torrent_file", id });
    }

    private async Task<IActionResult> HandleCoreAddTorrentMagnetAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0)
        {
            var magnetUri = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
            var optionsElem = paramsElem.GetArrayLength() > 1 ? paramsElem[1] : default;

            if (!string.IsNullOrWhiteSpace(magnetUri))
            {
                try
                {
                    var added = _torrentImportService.ImportFromMagnet(magnetUri);
                    ApplyDelugeOptions(added, optionsElem);
                    return DelugeResult(new { result = (added.InfoHash ?? string.Empty).ToLowerInvariant(), error = (object)null, id });
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to add magnet in Deluge RPC");
                    return DelugeResult(new { result = (object)null, error = ex.Message, id });
                }
            }
        }

        await Task.CompletedTask;
        return DelugeResult(new { result = (object)null, error = "Invalid arguments for add_torrent_magnet", id });
    }

    private async Task<IActionResult> HandleCoreAddTorrentUrlAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0)
        {
            var url = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
            var optionsElem = paramsElem.GetArrayLength() > 1 ? paramsElem[1] : default;

            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                    {
                        var added = _torrentImportService.ImportFromMagnet(url);
                        ApplyDelugeOptions(added, optionsElem);
                        return DelugeResult(new { result = (added.InfoHash ?? string.Empty).ToLowerInvariant(), error = (object)null, id });
                    }

                    var bytes = await _httpClient.GetByteArrayAsync(url);
                    using var ms = new MemoryStream(bytes);
                    var addedFromFile = _torrentImportService.ImportFromFile(ms, "downloaded.torrent");
                    ApplyDelugeOptions(addedFromFile, optionsElem);
                    return DelugeResult(new { result = (addedFromFile.InfoHash ?? string.Empty).ToLowerInvariant(), error = (object)null, id });
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to add torrent from URL in Deluge RPC");
                    return DelugeResult(new { result = (object)null, error = ex.Message, id });
                }
            }
        }

        return DelugeResult(new { result = (object)null, error = "Invalid arguments for add_torrent_url", id });
    }

    private async Task<IActionResult> HandleWebUploadTorrentAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
        {
            var fileName = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : "file.torrent";
            var fileDumpBase64 = paramsElem[1].ValueKind == JsonValueKind.String ? paramsElem[1].GetString() : null;

            if (!string.IsNullOrWhiteSpace(fileDumpBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(fileDumpBase64);
                    using var ms = new MemoryStream(bytes);
                    var added = _torrentImportService.ImportFromFile(ms, fileName);
                    return DelugeResult(new { result = (added.InfoHash ?? string.Empty).ToLowerInvariant(), error = (object)null, id });
                }
                catch (Exception ex)
                {
                    return DelugeResult(new { result = (object)null, error = ex.Message, id });
                }
            }
        }

        await Task.CompletedTask;
        return DelugeResult(new { result = (object)null, error = "Invalid upload arguments", id });
    }

    private async Task<IActionResult> HandleWebGetTorrentInfoAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0)
        {
            var pathOrDump = paramsElem[0].ValueKind == JsonValueKind.String ? paramsElem[0].GetString() : null;
            if (!string.IsNullOrWhiteSpace(pathOrDump))
            {
                try
                {
                    var bytes = Convert.FromBase64String(pathOrDump);
                    using var ms = new MemoryStream(bytes);
                    var parsed = _torrentFileParser.Parse(ms);
                    if (parsed != null)
                    {
                        return DelugeResult(new
                        {
                            result = new
                            {
                                name = parsed.Name,
                                size = parsed.TotalSize,
                                files_tree = new Dictionary<string, object>(),
                            },
                            error = (object)null,
                            id,
                        });
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }

        await Task.CompletedTask;
        return DelugeResult(new { result = new { name = "torrent", size = 0L, files_tree = new Dictionary<string, object>() }, error = (object)null, id });
    }

    private async Task<IActionResult> HandleWebAddTorrentsAsync(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0)
        {
            var torrentListElem = paramsElem[0];
            if (torrentListElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in torrentListElem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        var path = item.TryGetProperty("path", out var p) ? p.GetString() : null;
                        var opts = item.TryGetProperty("options", out var o) ? o : default;

                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            try
                            {
                                if (path.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
                                {
                                    var added = _torrentImportService.ImportFromMagnet(path);
                                    ApplyDelugeOptions(added, opts);
                                }
                                else
                                {
                                    var bytes = Convert.FromBase64String(path);
                                    using var ms = new MemoryStream(bytes);
                                    var added = _torrentImportService.ImportFromFile(ms, "file.torrent");
                                    ApplyDelugeOptions(added, opts);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn(ex, "Failed to add torrent item in web.add_torrents");
                            }
                        }
                    }
                }
            }
        }

        await Task.CompletedTask;
        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private void ApplyDelugeOptions(Torrent added, JsonElement options)
    {
        if (added == null || options.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var needsUpdate = false;
        if (options.TryGetProperty("download_location", out var dlProp) && dlProp.ValueKind == JsonValueKind.String)
        {
            added.SourcePath = dlProp.GetString();
            needsUpdate = true;
        }

        if (options.TryGetProperty("add_paused", out var apProp) && apProp.ValueKind == JsonValueKind.True)
        {
            added.Pause();
            needsUpdate = true;
        }

        if (options.TryGetProperty("label", out var lblProp) && lblProp.ValueKind == JsonValueKind.String)
        {
            added.Label = lblProp.GetString();
            needsUpdate = true;
        }

        if (needsUpdate)
        {
            _torrentService.Update(added);
        }
    }

    private IActionResult HandleCorePauseTorrents(string method, JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashesFromParams(paramsElem);
        var allTorrents = _torrentService.GetAll();

        if (hashes.Count == 0 || method == "core.pause_all_torrents")
        {
            foreach (var t in allTorrents)
            {
                t.Pause();
                _torrentService.Update(t);
            }
        }
        else
        {
            foreach (var t in allTorrents.Where(t => hashes.Contains((t.InfoHash ?? string.Empty).ToLowerInvariant())))
            {
                t.Pause();
                _torrentService.Update(t);
            }
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreResumeTorrents(string method, JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashesFromParams(paramsElem);
        var allTorrents = _torrentService.GetAll();

        if (hashes.Count == 0 || method == "core.resume_all_torrents")
        {
            foreach (var t in allTorrents)
            {
                t.Resume();
                _torrentService.Update(t);
            }
        }
        else
        {
            foreach (var t in allTorrents.Where(t => hashes.Contains((t.InfoHash ?? string.Empty).ToLowerInvariant())))
            {
                t.Resume();
                _torrentService.Update(t);
            }
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreRemoveTorrents(string method, JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashesFromParams(paramsElem);
        var removeData = false;

        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 1 &&
            (paramsElem[1].ValueKind == JsonValueKind.True || paramsElem[1].ValueKind == JsonValueKind.False))
        {
            removeData = paramsElem[1].GetBoolean();
        }

        var allTorrents = _torrentService.GetAll();
        foreach (var t in allTorrents.Where(t => hashes.Contains((t.InfoHash ?? string.Empty).ToLowerInvariant())))
        {
            _torrentService.Delete(t.Id, removeData);
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreForceRecheck(JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashesFromParams(paramsElem);
        var allTorrents = _torrentService.GetAll();
        foreach (var t in allTorrents.Where(t => hashes.Contains((t.InfoHash ?? string.Empty).ToLowerInvariant())))
        {
            _torrentService.Recheck(t.Id);
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreForceReannounce(JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashesFromParams(paramsElem);
        var allTorrents = _torrentService.GetAll();
        foreach (var t in allTorrents.Where(t => hashes.Contains((t.InfoHash ?? string.Empty).ToLowerInvariant())))
        {
            t.LastActive = DateTime.UtcNow;
            _torrentService.Update(t);
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreMoveStorage(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
        {
            var hashes = ExtractHashesFromParams(paramsElem);
            var dest = paramsElem[1].ValueKind == JsonValueKind.String ? paramsElem[1].GetString() : null;

            if (!string.IsNullOrWhiteSpace(dest))
            {
                var allTorrents = _torrentService.GetAll();
                foreach (var t in allTorrents.Where(t => hashes.Contains((t.InfoHash ?? string.Empty).ToLowerInvariant())))
                {
                    t.SourcePath = dest;
                    _torrentService.Update(t);
                }
            }
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreSetTorrentOptions(JsonElement paramsElem, object id)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() >= 2)
        {
            var hashes = ExtractHashesFromParams(paramsElem);
            var opts = paramsElem[1];
            if (opts.ValueKind == JsonValueKind.Object)
            {
                var allTorrents = _torrentService.GetAll();
                foreach (var t in allTorrents.Where(t => hashes.Contains((t.InfoHash ?? string.Empty).ToLowerInvariant())))
                {
                    ApplyDelugeOptions(t, opts);
                }
            }
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreSetTorrentFilePriorities(JsonElement paramsElem, object id)
    {
        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreRenameFiles(JsonElement paramsElem, object id)
    {
        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleCoreQueue(string method, JsonElement paramsElem, object id)
    {
        var hashes = ExtractHashesFromParams(paramsElem);
        var position = method switch
        {
            "core.queue_top" => "top",
            "core.queue_up" => "up",
            "core.queue_down" => "down",
            "core.queue_bottom" => "bottom",
            _ => "top",
        };

        var allTorrents = _torrentService.GetAll();
        foreach (var t in allTorrents.Where(t => hashes.Contains((t.InfoHash ?? string.Empty).ToLowerInvariant())))
        {
            _torrentService.MoveQueue(t.Id, position);
        }

        return DelugeResult(new { result = true, error = (object)null, id });
    }

    private IActionResult HandleGetFilterTree(object id)
    {
        var allTorrents = _torrentService.GetAll();
        return DelugeResult(new { result = BuildFilterTree(allTorrents), error = (object)null, id });
    }

    private IActionResult HandleUnknownMethod(string method, object id)
    {
        _logger.Warn("Unhandled Deluge RPC method: {0}", method);
        return DelugeResult(new { result = (object)null, error = $"Method '{method}' not implemented", id });
    }

    private Dictionary<string, object> GetDelugeConfigDictionary()
    {
        return new Dictionary<string, object>
        {
            ["max_download_speed"] = (double)(_configService?.MaxDownloadSpeedKbps ?? 1250),
            ["max_upload_speed"] = (double)(_configService?.MaxUploadSpeedKbps ?? 625),
            ["download_location"] = _configService?.WatchFolderPath ?? "/downloads",
            ["move_completed_path"] = _configService?.WatchFolderPath ?? "/downloads",
            ["move_completed"] = false,
            ["max_connections_global"] = _configService?.MaxGlobalConnections ?? 200,
            ["max_connections_per_torrent"] = _configService?.MaxPerTorrentConnections ?? 50,
            ["max_upload_slots_global"] = 4,
            ["max_active_limit"] = 10,
            ["max_active_downloading"] = 5,
            ["max_active_seeding"] = 5,
            ["listen_ports"] = new[] { _configService?.ListeningPort ?? 6881, _configService?.ListeningPort ?? 6881 },
            ["dht"] = _configService?.EnableDht ?? true,
            ["upnp"] = true,
            ["natpmp"] = true,
            ["lsd"] = _configService?.EnableLpd ?? true,
            ["utpex"] = _configService?.EnablePex ?? true,
            ["enc_in_policy"] = 1,
            ["enc_out_policy"] = 1,
            ["enc_level"] = 2,
            ["stop_seed_at_ratio"] = false,
            ["stop_seed_ratio"] = 2.0,
            ["remove_seed_at_ratio"] = false,
            ["auto_managed"] = true,
        };
    }

    private Dictionary<string, object> MapTorrentToDelugeStatus(Torrent t, HashSet<string> keys)
    {
        var status = new Dictionary<string, object>
        {
            ["name"] = t.Name ?? string.Empty,
            ["total_size"] = t.TotalSize,
            ["progress"] = t.Progress * 100.0,
            ["state"] = MapToDelugeState(t.Status, t.Progress),
            ["download_payload_rate"] = t.DownloadSpeed,
            ["upload_payload_rate"] = t.UploadSpeed,
            ["eta"] = CalculateEta(t),
            ["ratio"] = t.Ratio,
            ["num_seeds"] = t.Seeders,
            ["total_seeds"] = t.Seeders,
            ["num_peers"] = t.Leechers,
            ["total_peers"] = t.Leechers,
            ["total_done"] = t.Downloaded,
            ["total_uploaded"] = t.Uploaded,
            ["total_payload_download"] = t.Downloaded,
            ["total_payload_upload"] = t.Uploaded,
            ["save_path"] = !string.IsNullOrWhiteSpace(t.SourcePath) ? t.SourcePath : (_configService?.WatchFolderPath ?? "/downloads"),
            ["download_location"] = !string.IsNullOrWhiteSpace(t.SourcePath) ? t.SourcePath : (_configService?.WatchFolderPath ?? "/downloads"),
            ["label"] = t.Label ?? string.Empty,
            ["time_since_transfer"] = 0,
            ["time_added"] = new DateTimeOffset(t.DateAdded).ToUnixTimeSeconds(),
            ["is_finished"] = t.Progress >= 1.0,
            ["is_auto_managed"] = true,
            ["stop_at_ratio"] = false,
            ["stop_ratio"] = 2.0,
            ["remove_at_ratio"] = false,
            ["max_download_speed"] = t.DownloadLimit,
            ["max_upload_speed"] = t.UploadLimit,
            ["num_files"] = 1,
            ["tracker_status"] = "Announce OK",
            ["tracker_host"] = t.TrackerUrl ?? string.Empty,
            ["queue"] = t.SortOrder,
            ["active_time"] = (int)t.SeedingTime,
            ["seeding_time"] = (int)t.SeedingTime,
            ["message"] = string.Empty,
        };

        if (keys == null || keys.Count == 0)
        {
            return status;
        }

        var filtered = new Dictionary<string, object>();
        foreach (var k in keys)
        {
            if (status.TryGetValue(k, out var val))
            {
                filtered[k] = val;
            }
        }

        return filtered;
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

    private static string MapToDelugeState(TorrentStatus status, double progress)
    {
        return status switch
        {
            TorrentStatus.Queued => "Queued",
            TorrentStatus.Downloading => "Downloading",
            TorrentStatus.Seeding => "Seeding",
            TorrentStatus.Paused => "Paused",
            TorrentStatus.Stopped => "Paused",
            TorrentStatus.Error => "Error",
            _ => "Active",
        };
    }

    private static (JsonElement FilterObj, HashSet<string> Keys) ParseStatusParams(JsonElement paramsElem, bool isWebUpdateUi)
    {
        JsonElement filterObj = default;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (paramsElem.ValueKind == JsonValueKind.Array)
        {
            var len = paramsElem.GetArrayLength();
            if (len > 0)
            {
                if (paramsElem[0].ValueKind == JsonValueKind.Array && isWebUpdateUi)
                {
                    foreach (var k in paramsElem[0].EnumerateArray())
                    {
                        if (k.ValueKind == JsonValueKind.String)
                        {
                            keys.Add(k.GetString());
                        }
                    }
                }
                else if (paramsElem[0].ValueKind == JsonValueKind.Object)
                {
                    filterObj = paramsElem[0];
                }
            }

            if (len > 1)
            {
                if (paramsElem[1].ValueKind == JsonValueKind.Array)
                {
                    foreach (var k in paramsElem[1].EnumerateArray())
                    {
                        if (k.ValueKind == JsonValueKind.String)
                        {
                            keys.Add(k.GetString());
                        }
                    }
                }
                else if (paramsElem[1].ValueKind == JsonValueKind.Object && isWebUpdateUi)
                {
                    filterObj = paramsElem[1];
                }
            }
        }

        return (filterObj, keys);
    }

    private static List<Torrent> FilterTorrents(List<Torrent> torrents, JsonElement filterObj)
    {
        if (filterObj.ValueKind != JsonValueKind.Object)
        {
            return torrents;
        }

        var result = torrents.AsEnumerable();

        if (filterObj.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
        {
            var targetState = stateProp.GetString();
            if (!string.IsNullOrEmpty(targetState) && !string.Equals(targetState, "All", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Where(t => string.Equals(MapToDelugeState(t.Status, t.Progress), targetState, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (filterObj.TryGetProperty("label", out var labelProp) && labelProp.ValueKind == JsonValueKind.String)
        {
            var targetLabel = labelProp.GetString();
            if (!string.IsNullOrEmpty(targetLabel) && !string.Equals(targetLabel, "All", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Where(t => string.Equals(t.Label, targetLabel, StringComparison.OrdinalIgnoreCase));
            }
        }

        return result.ToList();
    }

    private Dictionary<string, object> BuildFilterTree(List<Torrent> torrents)
    {
        var states = new Dictionary<string, int>
        {
            ["All"] = torrents.Count,
            ["Downloading"] = torrents.Count(t => t.Status == TorrentStatus.Downloading),
            ["Seeding"] = torrents.Count(t => t.Status == TorrentStatus.Seeding || t.Progress >= 1.0),
            ["Paused"] = torrents.Count(t => t.Status == TorrentStatus.Paused || t.Status == TorrentStatus.Stopped),
            ["Active"] = torrents.Count(t => t.DownloadSpeed > 0 || t.UploadSpeed > 0),
            ["Error"] = torrents.Count(t => t.Status == TorrentStatus.Error),
        };

        var labels = new Dictionary<string, int>
        {
            ["All"] = torrents.Count,
        };

        foreach (var t in torrents)
        {
            if (!string.IsNullOrWhiteSpace(t.Label))
            {
                labels[t.Label] = labels.GetValueOrDefault(t.Label, 0) + 1;
            }
        }

        return new Dictionary<string, object>
        {
            ["state"] = states.Select(kvp => new object[] { kvp.Key, kvp.Value }).ToArray(),
            ["label"] = labels.Select(kvp => new object[] { kvp.Key, kvp.Value }).ToArray(),
            ["tracker_host"] = new object[] { new object[] { "All", torrents.Count } },
        };
    }

    private static HashSet<string> ExtractHashesFromParams(JsonElement paramsElem)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0)
        {
            var first = paramsElem[0];
            if (first.ValueKind == JsonValueKind.String)
            {
                var str = first.GetString();
                if (!string.IsNullOrEmpty(str))
                {
                    set.Add(str.ToLowerInvariant());
                }
            }
            else if (first.ValueKind == JsonValueKind.Array)
            {
                foreach (var h in first.EnumerateArray())
                {
                    if (h.ValueKind == JsonValueKind.String)
                    {
                        var str = h.GetString();
                        if (!string.IsNullOrEmpty(str))
                        {
                            set.Add(str.ToLowerInvariant());
                        }
                    }
                }
            }
        }

        return set;
    }

    private static string GetFirstStringParam(JsonElement paramsElem)
    {
        if (paramsElem.ValueKind == JsonValueKind.Array && paramsElem.GetArrayLength() > 0 &&
            paramsElem[0].ValueKind == JsonValueKind.String)
        {
            return paramsElem[0].GetString();
        }

        return null;
    }
}
