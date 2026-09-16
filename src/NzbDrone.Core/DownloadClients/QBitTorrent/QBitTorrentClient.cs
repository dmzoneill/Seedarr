using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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

    public QBitTorrentClient(HttpClient client = null)
    {
        _logger = LogManager.GetCurrentClassLogger();
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

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/auth/login") { Content = content };
            using var response = _client.Send(request);
            using var reader = new StreamReader(response.Content.ReadAsStream());
            var body = reader.ReadToEnd();
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

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = _client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return items;
            }

            using var stream = response.Content.ReadAsStream();
            using var torrents = JsonDocument.Parse(stream);

            foreach (var t in torrents.RootElement.EnumerateArray())
            {
                var state = t.TryGetProperty("state", out var s) ? s.GetString() : "unknown";

                var isPrivate = t.TryGetProperty("is_private", out var ip) &&
                    (ip.ValueKind == JsonValueKind.True ||
                     (ip.ValueKind == JsonValueKind.Number && ip.GetInt64() != 0) ||
                     (ip.ValueKind == JsonValueKind.String && bool.TryParse(ip.GetString(), out var pb) && pb));

                items.Add(new DownloadClientItem
                {
                    InfoHash = t.TryGetProperty("hash", out var h) ? h.GetString() : "",
                    Title = t.TryGetProperty("name", out var n) ? n.GetString() : "",
                    TotalSize = t.TryGetProperty("total_size", out var ts) ? ts.GetInt64() : 0,
                    RemainingSize = t.TryGetProperty("amount_left", out var al) ? al.GetInt64() : 0,
                    Status = MapState(state),
                    OutputPath = t.TryGetProperty("save_path", out var sp) ? sp.GetString() : "",
                    Category = t.TryGetProperty("category", out var c) ? c.GetString() : "",
                    IsPrivate = isPrivate,
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
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v2/torrents/export?hash={infoHash}");
            using var response = _client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("qBittorrent export failed for {0}: {1}", infoHash, response.StatusCode);
                return null;
            }

            using var ms = new MemoryStream();
            response.Content.ReadAsStream().CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export .torrent from qBittorrent: {0}", infoHash);
            return null;
        }
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
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v2/torrents/trackers?hash={infoHash}");
            using var response = _client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return trackers;
            }

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);
            foreach (var tr in doc.RootElement.EnumerateArray())
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
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to get trackers from qBittorrent for {0}", infoHash);
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
            var trackerList = string.Join("\n", trackers);
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hash", infoHash),
                new KeyValuePair<string, string>("urls", trackerList)
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/addTrackers") { Content = content };
            using var response = _client.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add trackers to qBittorrent for torrent {0}", infoHash);
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
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", infoHash)
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/reannounce") { Content = content };
            using var response = _client.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to reannounce qBittorrent torrent {0}", infoHash);
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
            using var versionReq = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v2/app/version");
            using var versionResp = _client.Send(versionReq);
            if (versionResp.IsSuccessStatusCode)
            {
                using var reader = new StreamReader(versionResp.Content.ReadAsStream());
                var version = reader.ReadToEnd();
                var verStr = string.IsNullOrWhiteSpace(version) ? "" : $" {version.Trim()}";
                return DownloadClientTestResult.Ok($"Successfully connected to qBittorrent{verStr} at {BaseUrl}");
            }

            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", Username),
                new KeyValuePair<string, string>("password", Password),
            });

            using var authReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/auth/login") { Content = content };
            using var response = _client.Send(authReq);
            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return DownloadClientTestResult.Fail($"Authentication failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase}). Please check username and password.");
            }

            using var bodyReader = new StreamReader(response.Content.ReadAsStream());
            var body = bodyReader.ReadToEnd();
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

    public async Task<DownloadClientSpeedLimits> GetSpeedLimitsAsync(CancellationToken cancellationToken = default)
    {
        var result = new DownloadClientSpeedLimits();
        if (!Authenticate())
        {
            return result;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v2/transfer/info");
            using var response = await _client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("up_info_speed", out var upSpeed) && upSpeed.TryGetInt64(out var upSpeedVal))
            {
                result.CurrentUploadRateBps = upSpeedVal;
            }

            if (root.TryGetProperty("dl_info_speed", out var dlSpeed) && dlSpeed.TryGetInt64(out var dlSpeedVal))
            {
                result.CurrentDownloadRateBps = dlSpeedVal;
            }

            if (root.TryGetProperty("up_rate_limit", out var upLimit) && upLimit.TryGetInt64(out var upLimitVal))
            {
                result.UploadLimitBps = upLimitVal > 0 ? upLimitVal : null;
            }

            if (root.TryGetProperty("dl_rate_limit", out var dlLimit) && dlLimit.TryGetInt64(out var dlLimitVal))
            {
                result.DownloadLimitBps = dlLimitVal > 0 ? dlLimitVal : null;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get speed limits from qBittorrent");
        }

        return result;
    }

    public async Task SetSpeedLimitsAsync(long? uploadBps, long? downloadBps, CancellationToken cancellationToken = default)
    {
        if (!Authenticate())
        {
            return;
        }

        try
        {
            if (uploadBps.HasValue)
            {
                using var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("limit", Math.Max(0, uploadBps.Value).ToString())
                });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/transfer/setUploadLimit") { Content = content };
                using var response = await _client.SendAsync(request, cancellationToken);
            }

            if (downloadBps.HasValue)
            {
                using var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("limit", Math.Max(0, downloadBps.Value).ToString())
                });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/transfer/setDownloadLimit") { Content = content };
                using var response = await _client.SendAsync(request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set speed limits in qBittorrent");
        }
    }

    public async Task SetTorrentLimitsAsync(string infoHash, long? uploadBps, long? downloadBps, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(infoHash) || !Authenticate())
        {
            return;
        }

        try
        {
            if (uploadBps.HasValue)
            {
                using var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", infoHash),
                    new KeyValuePair<string, string>("limit", (uploadBps.Value > 0 ? uploadBps.Value : 0).ToString())
                });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/setUploadLimit") { Content = content };
                using var response = await _client.SendAsync(request, cancellationToken);
            }

            if (downloadBps.HasValue)
            {
                using var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", infoHash),
                    new KeyValuePair<string, string>("limit", (downloadBps.Value > 0 ? downloadBps.Value : 0).ToString())
                });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/setDownloadLimit") { Content = content };
                using var response = await _client.SendAsync(request, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set torrent limits in qBittorrent for {0}", infoHash);
        }
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
