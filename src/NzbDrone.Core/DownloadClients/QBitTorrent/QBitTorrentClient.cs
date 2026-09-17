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
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private HttpClient _client;
    private volatile bool _isAuthenticated;
    private bool _disposed;

    private string _host = "localhost";
    private int _port = 8080;
    private bool _useSsl;
    private string _username = "admin";
    private string _password = "adminadmin";
    private string _category = "";

    public string Name => "qBittorrent";
    public string ClientType => "QBitTorrent";

    public string Host
    {
        get => _host;
        set
        {
            if (_host != value)
            {
                _host = value;
                _isAuthenticated = false;
            }
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            if (_port != value)
            {
                _port = value;
                _isAuthenticated = false;
            }
        }
    }

    public bool UseSsl
    {
        get => _useSsl;
        set
        {
            if (_useSsl != value)
            {
                _useSsl = value;
                _isAuthenticated = false;
            }
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            if (_username != value)
            {
                _username = value;
                _isAuthenticated = false;
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (_password != value)
            {
                _password = value;
                _isAuthenticated = false;
            }
        }
    }

    public string Category
    {
        get => _category;
        set => _category = value;
    }

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

    private bool Authenticate(bool force = false)
    {
        return EnsureAuthenticated(force);
    }

    private bool EnsureAuthenticated(bool force = false)
    {
        if (_isAuthenticated && !force)
        {
            return true;
        }

        _authLock.Wait();
        try
        {
            if (_isAuthenticated && !force)
            {
                return true;
            }

            _isAuthenticated = false;
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", Username),
                new KeyValuePair<string, string>("password", Password),
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/auth/login") { Content = content };
            using var response = _client.Send(request);
            using var reader = new StreamReader(response.Content.ReadAsStream());
            var body = reader.ReadToEnd();
            var success = response.IsSuccessStatusCode && body.Contains("Ok");
            _isAuthenticated = success;
            return success;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "qBittorrent auth failed");
            _isAuthenticated = false;
            return false;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task<bool> EnsureAuthenticatedAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (_isAuthenticated && !force)
        {
            return true;
        }

        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (_isAuthenticated && !force)
            {
                return true;
            }

            _isAuthenticated = false;
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", Username),
                new KeyValuePair<string, string>("password", Password),
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/auth/login") { Content = content };
            using var response = await _client.SendAsync(request, cancellationToken);
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var success = response.IsSuccessStatusCode && body.Contains("Ok");
            _isAuthenticated = success;
            return success;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "qBittorrent auth failed");
            _isAuthenticated = false;
            return false;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private HttpResponseMessage SendWithAuth(Func<HttpRequestMessage> requestFactory)
    {
        if (!EnsureAuthenticated())
        {
            return null;
        }

        var response = _client.Send(requestFactory());
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            response.Dispose();
            if (EnsureAuthenticated(force: true))
            {
                return _client.Send(requestFactory());
            }

            return null;
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken = default)
    {
        if (!await EnsureAuthenticatedAsync(cancellationToken: cancellationToken))
        {
            return null;
        }

        var response = await _client.SendAsync(requestFactory(), cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            response.Dispose();
            if (await EnsureAuthenticatedAsync(force: true, cancellationToken: cancellationToken))
            {
                return await _client.SendAsync(requestFactory(), cancellationToken);
            }

            return null;
        }

        return response;
    }

    public List<DownloadClientItem> GetItems()
    {
        var items = new List<DownloadClientItem>();

        try
        {
            var url = $"{BaseUrl}/api/v2/torrents/info";
            if (!string.IsNullOrEmpty(Category))
            {
                url += $"?category={Uri.EscapeDataString(Category)}";
            }

            using var response = SendWithAuth(() => new HttpRequestMessage(HttpMethod.Get, url));
            if (response == null || !response.IsSuccessStatusCode)
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
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return null;
        }

        try
        {
            using var response = SendWithAuth(() => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v2/torrents/export?hash={infoHash}"));
            if (response == null || !response.IsSuccessStatusCode)
            {
                _logger.Warn("qBittorrent export failed for {0}: {1}", infoHash, response?.StatusCode);
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
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return trackers;
        }

        try
        {
            using var response = SendWithAuth(() => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v2/torrents/trackers?hash={infoHash}"));
            if (response == null || !response.IsSuccessStatusCode)
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

        try
        {
            var trackerList = string.Join("\n", trackers);
            using var response = SendWithAuth(() =>
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hash", infoHash),
                    new KeyValuePair<string, string>("urls", trackerList),
                });
                return new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/addTrackers") { Content = content };
            });

            return response != null && response.IsSuccessStatusCode;
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

        try
        {
            using var response = SendWithAuth(() =>
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("hashes", infoHash),
                });
                return new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/reannounce") { Content = content };
            });

            return response != null && response.IsSuccessStatusCode;
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
                _isAuthenticated = false;
                return DownloadClientTestResult.Fail($"Authentication failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase}). Please check username and password.");
            }

            using var bodyReader = new StreamReader(response.Content.ReadAsStream());
            var body = bodyReader.ReadToEnd();
            if (response.IsSuccessStatusCode && body.Contains("Ok"))
            {
                _isAuthenticated = true;
                return DownloadClientTestResult.Ok($"Successfully connected to qBittorrent at {BaseUrl}");
            }

            _isAuthenticated = false;
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client?.Dispose();
        _authLock.Dispose();
    }

    public async Task<DownloadClientSpeedLimits> GetSpeedLimitsAsync(CancellationToken cancellationToken = default)
    {
        var result = new DownloadClientSpeedLimits();

        try
        {
            using var response = await SendWithAuthAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v2/transfer/info"), cancellationToken);
            if (response == null || !response.IsSuccessStatusCode)
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
        try
        {
            if (uploadBps.HasValue)
            {
                using var response = await SendWithAuthAsync(
                    () =>
                    {
                        var content = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("limit", Math.Max(0, uploadBps.Value).ToString()),
                        });
                        return new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/transfer/setUploadLimit") { Content = content };
                    },
                    cancellationToken);
            }

            if (downloadBps.HasValue)
            {
                using var response = await SendWithAuthAsync(
                    () =>
                    {
                        var content = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("limit", Math.Max(0, downloadBps.Value).ToString()),
                        });
                        return new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/transfer/setDownloadLimit") { Content = content };
                    },
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set speed limits in qBittorrent");
        }
    }

    public async Task SetTorrentLimitsAsync(string infoHash, long? uploadBps, long? downloadBps, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(infoHash))
        {
            return;
        }

        try
        {
            if (uploadBps.HasValue)
            {
                using var response = await SendWithAuthAsync(
                    () =>
                    {
                        var content = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("hashes", infoHash),
                            new KeyValuePair<string, string>("limit", (uploadBps.Value > 0 ? uploadBps.Value : 0).ToString()),
                        });
                        return new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/setUploadLimit") { Content = content };
                    },
                    cancellationToken);
            }

            if (downloadBps.HasValue)
            {
                using var response = await SendWithAuthAsync(
                    () =>
                    {
                        var content = new FormUrlEncodedContent(new[]
                        {
                            new KeyValuePair<string, string>("hashes", infoHash),
                            new KeyValuePair<string, string>("limit", (downloadBps.Value > 0 ? downloadBps.Value : 0).ToString()),
                        });
                        return new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v2/torrents/setDownloadLimit") { Content = content };
                    },
                    cancellationToken);
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
