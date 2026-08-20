using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

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

    public DelugeClient()
    {
        _logger = LogManager.GetCurrentClassLogger();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            CheckCertificateRevocationList = true,
        };

        _client = new HttpClient(handler);
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
        var response = Task.Run(() => _client.PostAsync(JsonUrl, content)).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var body = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
        return JsonDocument.Parse(body);
    }

    private bool Authenticate()
    {
        try
        {
            using var doc = SendRequest("auth.login", new object[] { Password });
            return doc.RootElement.TryGetProperty("result", out var result) && result.GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Deluge auth failed");
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
            var fields = new[] { "hash", "name", "total_size", "total_remaining", "state", "save_path", "label" };
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

                items.Add(new DownloadClientItem
                {
                    InfoHash = prop.Name,
                    Title = t.TryGetProperty("name", out var n) ? n.GetString() : "",
                    TotalSize = t.TryGetProperty("total_size", out var ts) ? ts.GetInt64() : 0,
                    RemainingSize = t.TryGetProperty("total_remaining", out var tr) ? tr.GetInt64() : 0,
                    Status = MapState(state),
                    OutputPath = t.TryGetProperty("save_path", out var sp) ? sp.GetString() : "",
                    Category = t.TryGetProperty("label", out var l) ? l.GetString() : "",
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
        _logger.Warn("Deluge does not support direct .torrent export via Web API for hash: {0}", infoHash);
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
