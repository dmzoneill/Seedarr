using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.DownloadClients.Deluge;

public class DelugeClient : IDownloadClient, IDisposable
{
    private readonly Logger _logger;
    private readonly CookieContainer _cookies = new();
    private HttpClient _client;
    private int _requestId;

    public string Name => "Deluge";
    public string ClientType => "Deluge";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8112;
    public bool UseSsl { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "deluge";
    public string Category { get; set; } = "";
    public IRemotePathMappingService RemotePathMappingService { get; set; }
    public string LocalTorrentDirectory { get; set; }

    public DelugeClient(HttpClient client = null, IRemotePathMappingService remotePathMappingService = null)
    {
        _logger = LogManager.GetCurrentClassLogger();
        RemotePathMappingService = remotePathMappingService;
        if (client != null)
        {
            _client = client;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookies,
                CheckCertificateRevocationList = true,
            };

            _client = new HttpClient(handler);
        }
    }

    private string JsonUrl => $"{(UseSsl ? "https" : "http")}://{Host}:{Port}/json";

    private JsonDocument SendRequest(string method, object[] parameters)
    {
        var payload = new
        {
            method,
            @params = parameters,
            id = _requestId++,
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, JsonUrl) { Content = content };
        using var response = _client.Send(request);
        response.EnsureSuccessStatusCode();

        using var stream = response.Content.ReadAsStream();
        return JsonDocument.Parse(stream);
    }

    private async Task<JsonDocument> SendRequestAsync(string method, object[] parameters, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            method,
            @params = parameters,
            id = _requestId++,
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, JsonUrl) { Content = content };
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private bool Authenticate()
    {
        try
        {
            using var doc = SendRequest("auth.login", new object[] { Password });
            if (!doc.RootElement.TryGetProperty("result", out var result) || !result.GetBoolean())
            {
                return false;
            }

            return EnsureDaemonConnected();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Deluge auth failed");
            return false;
        }
    }

    private async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = await SendRequestAsync("auth.login", new object[] { Password }, cancellationToken);
            if (!doc.RootElement.TryGetProperty("result", out var result) || !result.GetBoolean())
            {
                return false;
            }

            return await EnsureDaemonConnectedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Deluge auth failed");
            return false;
        }
    }

    private bool EnsureDaemonConnected()
    {
        try
        {
            using var connectedDoc = SendRequest("web.connected", Array.Empty<object>());
            if (connectedDoc.RootElement.TryGetProperty("result", out var connectedRes) &&
                connectedRes.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            _logger.Debug("Deluge web is not connected to daemon, querying hosts...");
            using var hostsDoc = SendRequest("web.get_hosts", Array.Empty<object>());
            if (!hostsDoc.RootElement.TryGetProperty("result", out var hostsRes) || hostsRes.ValueKind != JsonValueKind.Array)
            {
                _logger.Warn("Failed to retrieve hosts from Deluge web");
                return false;
            }

            string targetHostId = null;
            foreach (var hostEl in hostsRes.EnumerateArray())
            {
                if (hostEl.ValueKind == JsonValueKind.Array && hostEl.GetArrayLength() >= 1)
                {
                    var hostId = hostEl[0].GetString();
                    var status = hostEl.GetArrayLength() >= 4 ? hostEl[3].GetString() : null;

                    if (string.Equals(status, "Online", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "Connected", StringComparison.OrdinalIgnoreCase))
                    {
                        targetHostId = hostId;
                        break;
                    }

                    targetHostId ??= hostId;
                }
            }

            if (string.IsNullOrEmpty(targetHostId))
            {
                _logger.Warn("No available Deluge daemon hosts found");
                return false;
            }

            _logger.Debug("Connecting Deluge web to daemon host: {0}", targetHostId);
            using (SendRequest("web.connect", new object[] { targetHostId }))
            {
            }

            using var verifyDoc = SendRequest("web.connected", Array.Empty<object>());
            var isConnected = verifyDoc.RootElement.TryGetProperty("result", out var verifyRes) &&
                verifyRes.ValueKind == JsonValueKind.True;

            if (!isConnected)
            {
                _logger.Warn("Failed to connect Deluge web to daemon host: {0}", targetHostId);
            }

            return isConnected;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to perform Deluge daemon connection handshake");
            return false;
        }
    }

    private async Task<bool> EnsureDaemonConnectedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connectedDoc = await SendRequestAsync("web.connected", Array.Empty<object>(), cancellationToken);
            if (connectedDoc.RootElement.TryGetProperty("result", out var connectedRes) &&
                connectedRes.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            _logger.Debug("Deluge web is not connected to daemon, querying hosts...");
            using var hostsDoc = await SendRequestAsync("web.get_hosts", Array.Empty<object>(), cancellationToken);
            if (!hostsDoc.RootElement.TryGetProperty("result", out var hostsRes) || hostsRes.ValueKind != JsonValueKind.Array)
            {
                _logger.Warn("Failed to retrieve hosts from Deluge web");
                return false;
            }

            string targetHostId = null;
            foreach (var hostEl in hostsRes.EnumerateArray())
            {
                if (hostEl.ValueKind == JsonValueKind.Array && hostEl.GetArrayLength() >= 1)
                {
                    var hostId = hostEl[0].GetString();
                    var status = hostEl.GetArrayLength() >= 4 ? hostEl[3].GetString() : null;

                    if (string.Equals(status, "Online", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "Connected", StringComparison.OrdinalIgnoreCase))
                    {
                        targetHostId = hostId;
                        break;
                    }

                    targetHostId ??= hostId;
                }
            }

            if (string.IsNullOrEmpty(targetHostId))
            {
                _logger.Warn("No available Deluge daemon hosts found");
                return false;
            }

            _logger.Debug("Connecting Deluge web to daemon host: {0}", targetHostId);
            using (await SendRequestAsync("web.connect", new object[] { targetHostId }, cancellationToken))
            {
            }

            using var verifyDoc = await SendRequestAsync("web.connected", Array.Empty<object>(), cancellationToken);
            var isConnected = verifyDoc.RootElement.TryGetProperty("result", out var verifyRes) &&
                verifyRes.ValueKind == JsonValueKind.True;

            if (!isConnected)
            {
                _logger.Warn("Failed to connect Deluge web to daemon host: {0}", targetHostId);
            }

            return isConnected;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to perform Deluge daemon connection handshake");
            return false;
        }
    }

    public List<DownloadClientItem> GetItems()
    {
        var items = new List<DownloadClientItem>();

        if (!Authenticate())
        {
            return items;
        }

        try
        {
            var fields = new[] { "hash", "name", "total_size", "total_remaining", "state", "save_path", "label", "private" };
            var filters = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(Category))
            {
                filters["label"] = Category;
            }

            using var doc = SendRequest("web.update_ui", new object[] { fields, filters });

            if (!doc.RootElement.TryGetProperty("result", out var result))
            {
                return items;
            }

            if (!result.TryGetProperty("torrents", out var torrents))
            {
                return items;
            }

            foreach (var prop in torrents.EnumerateObject())
            {
                var t = prop.Value;
                var state = t.TryGetProperty("state", out var s) ? s.GetString() : "unknown";
                var isPrivate = t.TryGetProperty("private", out var ip) &&
                    (ip.ValueKind == JsonValueKind.True ||
                     (ip.ValueKind == JsonValueKind.Number && ip.GetInt64() != 0) ||
                     (ip.ValueKind == JsonValueKind.String && bool.TryParse(ip.GetString(), out var pb) && pb));

                items.Add(new DownloadClientItem
                {
                    InfoHash = prop.Name,
                    Title = t.TryGetProperty("name", out var n) ? n.GetString() : "",
                    TotalSize = t.TryGetProperty("total_size", out var ts) ? ts.GetInt64() : 0,
                    RemainingSize = t.TryGetProperty("total_remaining", out var tr) ? tr.GetInt64() : 0,
                    Status = MapState(state),
                    OutputPath = t.TryGetProperty("save_path", out var sp) ? sp.GetString() : "",
                    Category = t.TryGetProperty("label", out var l) ? l.GetString() : "",
                    IsPrivate = isPrivate,
                });
            }

            _logger.Debug("Fetched {0} items from Deluge", items.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch Deluge items");
        }

        return items;
    }

    public byte[] GetTorrentFile(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        try
        {
            if (!string.IsNullOrEmpty(LocalTorrentDirectory))
            {
                var candidate = Path.Combine(LocalTorrentDirectory, $"{infoHash}.torrent");
                if (File.Exists(candidate))
                {
                    return File.ReadAllBytes(candidate);
                }

                var lowerCandidate = Path.Combine(LocalTorrentDirectory, $"{infoHash.ToLowerInvariant()}.torrent");
                if (File.Exists(lowerCandidate))
                {
                    return File.ReadAllBytes(lowerCandidate);
                }
            }

            if (Authenticate())
            {
                try
                {
                    using var doc = SendRequest("core.get_torrent_status", new object[] { infoHash, new[] { "torrent_file" } });
                    if (doc.RootElement.TryGetProperty("result", out var result) &&
                        result.TryGetProperty("torrent_file", out var tfProp))
                    {
                        var filePath = tfProp.GetString();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            if (File.Exists(filePath))
                            {
                                return File.ReadAllBytes(filePath);
                            }

                            if (RemotePathMappingService != null)
                            {
                                var remapped = RemotePathMappingService.Remap(Host, filePath);
                                if (!string.IsNullOrEmpty(remapped) && File.Exists(remapped))
                                {
                                    return File.ReadAllBytes(remapped);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Deluge core.get_torrent_status did not provide accessible torrent_file for {0}", infoHash);
                }
            }

            if (RemotePathMappingService != null)
            {
                var standardRemoteState = $"/config/state/{infoHash.ToLowerInvariant()}.torrent";
                var remapped = RemotePathMappingService.Remap(Host, standardRemoteState);
                if (!string.IsNullOrEmpty(remapped) && File.Exists(remapped))
                {
                    return File.ReadAllBytes(remapped);
                }
            }

            _logger.Debug("Deluge torrent file for {0} is not accessible locally or via RPC; continuing gracefully", infoHash);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Error attempting to retrieve Deluge torrent file for {0}", infoHash);
        }

        return null;
    }

    public List<string> GetTrackers(string infoHash)
    {
        var trackers = new List<string>();
        if (string.IsNullOrWhiteSpace(infoHash) || !Authenticate())
        {
            return trackers;
        }

        try
        {
            using var doc = SendRequest("core.get_torrent_status", new object[] { infoHash, new[] { "trackers" } });
            if (doc.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("trackers", out var trList))
            {
                foreach (var tr in trList.EnumerateArray())
                {
                    if (tr.TryGetProperty("url", out var urlProp))
                    {
                        var url = urlProp.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            trackers.Add(url.Trim());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to get trackers from Deluge for {0}", infoHash);
        }

        return trackers;
    }

    public bool AddTrackers(string infoHash, IEnumerable<string> trackers)
    {
        if (string.IsNullOrWhiteSpace(infoHash) || trackers == null)
        {
            return false;
        }

        if (!Authenticate())
        {
            return false;
        }

        try
        {
            var trackerObjects = new List<object>();
            var tier = 0;
            foreach (var t in trackers)
            {
                trackerObjects.Add(new { tier = tier++, url = t });
            }

            using var doc = SendRequest("core.set_torrent_trackers", new object[] { infoHash, trackerObjects.ToArray() });
            return doc.RootElement.TryGetProperty("result", out var res) && res.ValueKind != JsonValueKind.Null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add trackers to Deluge for torrent {0}", infoHash);
            return false;
        }
    }

    public bool Reannounce(string infoHash)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return false;
        }

        if (!Authenticate())
        {
            return false;
        }

        try
        {
            using var doc = SendRequest("core.force_reannounce", new object[] { new[] { infoHash } });
            return doc.RootElement.TryGetProperty("result", out var res) && res.ValueKind != JsonValueKind.Null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to force reannounce Deluge torrent {0}", infoHash);
            return false;
        }
    }

    public bool TestConnection()
    {
        return TestConnectionDetailed().Success;
    }

    public DownloadClientTestResult TestConnectionDetailed()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return DownloadClientTestResult.Fail("Host cannot be empty");
        }

        try
        {
            if (!Authenticate())
            {
                return DownloadClientTestResult.Fail("Authentication failed. Invalid Deluge web password.");
            }

            using var doc = SendRequest("daemon.get_method_list", Array.Empty<object>());
            if (doc.RootElement.TryGetProperty("result", out _))
            {
                try
                {
                    using var verDoc = SendRequest("web.get_api_version", Array.Empty<object>());
                    if (verDoc.RootElement.TryGetProperty("result", out var ver) && ver.ValueKind == JsonValueKind.String)
                    {
                        return DownloadClientTestResult.Ok($"Successfully connected to Deluge (Web API {ver.GetString()}) at {JsonUrl}");
                    }
                }
                catch
                {
                    // Ignore extra version fetch failure
                }

                return DownloadClientTestResult.Ok($"Successfully connected to Deluge at {JsonUrl}");
            }

            return DownloadClientTestResult.Fail("Deluge daemon.get_method_list returned unexpected result");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return DownloadClientTestResult.Fail($"Endpoint not found (HTTP 404 Not Found) at {JsonUrl}. Please verify the host and port.");
        }
        catch (HttpRequestException ex)
        {
            return DownloadClientTestResult.Fail($"Network error connecting to {JsonUrl}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return DownloadClientTestResult.Fail($"Connection timed out connecting to {JsonUrl} (exceeded 10s)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Deluge connection test failed");
            return DownloadClientTestResult.Fail($"Connection failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    public async Task<DownloadClientSpeedLimits> GetSpeedLimitsAsync(CancellationToken cancellationToken = default)
    {
        var result = new DownloadClientSpeedLimits();
        if (!await AuthenticateAsync(cancellationToken))
        {
            return result;
        }

        try
        {
            using var configDoc = await SendRequestAsync("core.get_config", Array.Empty<object>(), cancellationToken);
            if (configDoc.RootElement.TryGetProperty("result", out var configResult))
            {
                if (configResult.TryGetProperty("max_upload_speed", out var upSpeed) && upSpeed.TryGetDouble(out var upVal))
                {
                    result.UploadLimitBps = upVal > 0 ? (long)(upVal * 1024) : null;
                }

                if (configResult.TryGetProperty("max_download_speed", out var downSpeed) && downSpeed.TryGetDouble(out var downVal))
                {
                    result.DownloadLimitBps = downVal > 0 ? (long)(downVal * 1024) : null;
                }
            }

            using var statsDoc = await SendRequestAsync("core.get_session_status", new object[] { new[] { "upload_rate", "download_rate" } }, cancellationToken);
            if (statsDoc.RootElement.TryGetProperty("result", out var statsResult))
            {
                if (statsResult.TryGetProperty("upload_rate", out var curUp) && curUp.TryGetDouble(out var curUpVal))
                {
                    result.CurrentUploadRateBps = (long)curUpVal;
                }

                if (statsResult.TryGetProperty("download_rate", out var curDown) && curDown.TryGetDouble(out var curDownVal))
                {
                    result.CurrentDownloadRateBps = (long)curDownVal;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get speed limits from Deluge");
        }

        return result;
    }

    public async Task SetSpeedLimitsAsync(long? uploadBps, long? downloadBps, CancellationToken cancellationToken = default)
    {
        if (!await AuthenticateAsync(cancellationToken))
        {
            return;
        }

        try
        {
            var config = new Dictionary<string, object>();
            if (uploadBps.HasValue)
            {
                config["max_upload_speed"] = uploadBps.Value > 0 ? (double)uploadBps.Value / 1024.0 : -1.0;
            }

            if (downloadBps.HasValue)
            {
                config["max_download_speed"] = downloadBps.Value > 0 ? (double)downloadBps.Value / 1024.0 : -1.0;
            }

            if (config.Count > 0)
            {
                using var doc = await SendRequestAsync("core.set_config", new object[] { config }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set speed limits in Deluge");
        }
    }

    public async Task SetTorrentLimitsAsync(string infoHash, long? uploadBps, long? downloadBps, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(infoHash) || !await AuthenticateAsync(cancellationToken))
        {
            return;
        }

        try
        {
            var options = new Dictionary<string, object>();
            if (uploadBps.HasValue)
            {
                options["max_upload_speed"] = uploadBps.Value > 0 ? (double)uploadBps.Value / 1024.0 : -1.0;
            }

            if (downloadBps.HasValue)
            {
                options["max_download_speed"] = downloadBps.Value > 0 ? (double)downloadBps.Value / 1024.0 : -1.0;
            }

            using var doc = await SendRequestAsync("core.set_torrent_options", new object[] { new[] { infoHash }, options }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set torrent limits in Deluge for {0}", infoHash);
        }
    }

    private static string MapState(string delugeState)
    {
        return delugeState switch
        {
            "Seeding" => "seeding",
            "Downloading" => "downloading",
            "Paused" => "paused",
            "Checking" => "checking",
            "Queued" => "downloading",
            "Error" => "error",
            _ => "unknown",
        };
    }
}
