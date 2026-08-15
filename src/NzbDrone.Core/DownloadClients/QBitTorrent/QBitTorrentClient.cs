using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.DownloadClients.QBitTorrent;

public class QBitTorrentClient : IDownloadClient, IDisposable
{
    private readonly Logger _logger;
    private readonly CookieContainer _cookies = new();
    private HttpClient _client;

    public string Name => "qBittorrent";
    public string ClientType => "QBitTorrent";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8080;
    public bool UseSsl { get; set; }
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "adminadmin";
    public string Category { get; set; } = "";

    public QBitTorrentClient()
    {
        _logger = LogManager.GetCurrentClassLogger();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            CheckCertificateRevocationList = true,
        };

        _client = new HttpClient(handler);
    }

    private string BaseUrl => $"{(UseSsl ? "https" : "http")}://{Host}:{Port}";

    private bool Authenticate()
    {
        try
        {
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", Username),
                new KeyValuePair<string, string>("password", Password),
            });

            var response = _client.PostAsync($"{BaseUrl}/api/v2/auth/login", content).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return response.IsSuccessStatusCode && body.Contains("Ok");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "qBittorrent auth failed");
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
            var url = $"{BaseUrl}/api/v2/torrents/info";
            if (!string.IsNullOrEmpty(Category))
            {
                url += $"?category={Uri.EscapeDataString(Category)}";
            }

            var response = _client.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return items;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var torrents = JsonDocument.Parse(json);

            foreach (var t in torrents.RootElement.EnumerateArray())
            {
                var state = t.TryGetProperty("state", out var s) ? s.GetString() : "unknown";

                items.Add(new DownloadClientItem
                {
                    InfoHash = t.TryGetProperty("hash", out var h) ? h.GetString() : "",
                    Title = t.TryGetProperty("name", out var n) ? n.GetString() : "",
                    TotalSize = t.TryGetProperty("total_size", out var ts) ? ts.GetInt64() : 0,
                    RemainingSize = t.TryGetProperty("amount_left", out var al) ? al.GetInt64() : 0,
                    Status = MapState(state),
                    OutputPath = t.TryGetProperty("save_path", out var sp) ? sp.GetString() : "",
                    Category = t.TryGetProperty("category", out var c) ? c.GetString() : "",
                });
            }

            _logger.Debug("Fetched {0} items from qBittorrent", items.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch qBittorrent items");
        }

        return items;
    }

    public byte[] GetTorrentFile(string infoHash)
    {
        if (!Authenticate())
        {
            return null;
        }

        try
        {
            var response = _client.GetAsync($"{BaseUrl}/api/v2/torrents/export?hash={infoHash}").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("qBittorrent export failed for {0}: {1}", infoHash, response.StatusCode);
                return null;
            }

            return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export .torrent from qBittorrent: {0}", infoHash);
            return null;
        }
    }

    public bool TestConnection()
    {
        try
        {
            var response = _client.GetAsync($"{BaseUrl}/api/v2/app/version").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return Authenticate();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "qBittorrent connection test failed");
            return false;
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    private static string MapState(string qbtState)
    {
        return qbtState switch
        {
            "uploading" or "stalledUP" or "forcedUP" or "queuedUP" => "seeding",
            "downloading" or "stalledDL" or "forcedDL" or "queuedDL" => "downloading",
            "pausedUP" or "pausedDL" => "paused",
            "checkingUP" or "checkingDL" or "checkingResumeData" => "checking",
            _ => "unknown",
        };
    }
}
