using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.DownloadClients.Deluge;

public class DelugeClient : IDownloadClient
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
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = _client.PostAsync(JsonUrl, content).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonDocument.Parse(body);
    }

    private bool Authenticate()
    {
        try
        {
            var doc = SendRequest("auth.login", new object[] { Password });
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

            var doc = SendRequest("web.update_ui", new object[] { fields, filters });

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

    public bool TestConnection()
    {
        try
        {
            if (!Authenticate())
            {
                return false;
            }

            var doc = SendRequest("daemon.get_method_list", Array.Empty<object>());
            return doc.RootElement.TryGetProperty("result", out _);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Deluge connection test failed");
            return false;
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
