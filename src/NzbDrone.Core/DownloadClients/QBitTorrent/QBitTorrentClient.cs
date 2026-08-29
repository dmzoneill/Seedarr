using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
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

            var response = Task.Run(() => _client.PostAsync($"{BaseUrl}/api/v2/auth/login", content)).GetAwaiter().GetResult();
            var body = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
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

            var response = Task.Run(() => _client.GetAsync(url)).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return items;
            }

            var json = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
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
            var response = Task.Run(() => _client.GetAsync($"{BaseUrl}/api/v2/torrents/export?hash={infoHash}")).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("qBittorrent export failed for {0}: {1}", infoHash, response.StatusCode);
                return null;
            }

            return Task.Run(() => response.Content.ReadAsByteArrayAsync()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export .torrent from qBittorrent: {0}", infoHash);
            return null;
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
            var versionResp = Task.Run(() => _client.GetAsync($"{BaseUrl}/api/v2/app/version")).GetAwaiter().GetResult();
            if (versionResp.IsSuccessStatusCode)
            {
                var version = Task.Run(() => versionResp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
                var verStr = string.IsNullOrWhiteSpace(version) ? "" : $" {version.Trim()}";
                return DownloadClientTestResult.Ok($"Successfully connected to qBittorrent{verStr} at {BaseUrl}");
            }

            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", Username),
                new KeyValuePair<string, string>("password", Password),
            });

            var response = Task.Run(() => _client.PostAsync($"{BaseUrl}/api/v2/auth/login", content)).GetAwaiter().GetResult();
            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return DownloadClientTestResult.Fail($"Authentication failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase}). Please check username and password.");
            }

            var body = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode && body.Contains("Ok"))
            {
                return DownloadClientTestResult.Ok($"Successfully connected to qBittorrent at {BaseUrl}");
            }

            if (body.Contains("Fails"))
            {
                return DownloadClientTestResult.Fail("Authentication failed. Invalid username or password.");
            }

            return DownloadClientTestResult.Fail($"qBittorrent returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            return DownloadClientTestResult.Fail($"Network error connecting to {BaseUrl}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return DownloadClientTestResult.Fail($"Connection timed out connecting to {BaseUrl} (exceeded 10s)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "qBittorrent connection test failed");
            return DownloadClientTestResult.Fail($"Connection failed: {ex.Message}");
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
