using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.DownloadClients.Transmission;

public class TransmissionClient : IDownloadClient, IDisposable
{
    private readonly Logger _logger;
    private HttpClient _client;
    private string _sessionId;

    public string Name => "Transmission";
    public string ClientType => "Transmission";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 9091;
    public bool UseSsl { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Category { get; set; } = "";

    public TransmissionClient()
    {
        _logger = LogManager.GetCurrentClassLogger();
        var handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = true,
        };

        _client = new HttpClient(handler);
    }

    private string RpcUrl => $"{(UseSsl ? "https" : "http")}://{Host}:{Port}/transmission/rpc";

    private HttpRequestMessage CreateRequest(string method, object arguments)
    {
        var payload = new { method, arguments };
        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, RpcUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        if (!string.IsNullOrEmpty(_sessionId))
        {
            request.Headers.Add("X-Transmission-Session-Id", _sessionId);
        }

        return request;
    }

    private JsonDocument SendRequest(string method, object arguments)
    {
        var request = CreateRequest(method, arguments);
        var response = Task.Run(() => _client.SendAsync(request)).GetAwaiter().GetResult();

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            if (response.Headers.TryGetValues("X-Transmission-Session-Id", out var values))
            {
                _sessionId = string.Join("", values);
            }

            response.Dispose();
            request = CreateRequest(method, arguments);
            response = Task.Run(() => _client.SendAsync(request)).GetAwaiter().GetResult();
        }

        response.EnsureSuccessStatusCode();
        var body = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
        return JsonDocument.Parse(body);
    }

    public List<DownloadClientItem> GetItems()
    {
        var items = new List<DownloadClientItem>();

        try
        {
            var arguments = new
            {
                fields = new[] { "hashString", "name", "totalSize", "leftUntilDone", "status", "downloadDir", "labels" },
            };

            using var doc = SendRequest("torrent-get", arguments);
            var torrents = doc.RootElement
                .GetProperty("arguments")
                .GetProperty("torrents");

            foreach (var t in torrents.EnumerateArray())
            {
                var labels = new List<string>();
                if (t.TryGetProperty("labels", out var labelsEl))
                {
                    foreach (var label in labelsEl.EnumerateArray())
                    {
                        labels.Add(label.GetString());
                    }
                }

                if (!string.IsNullOrEmpty(Category) && !labels.Contains(Category))
                {
                    continue;
                }

                var status = t.TryGetProperty("status", out var st) ? st.GetInt32() : 0;

                items.Add(new DownloadClientItem
                {
                    InfoHash = t.TryGetProperty("hashString", out var h) ? h.GetString() : "",
                    Title = t.TryGetProperty("name", out var n) ? n.GetString() : "",
                    TotalSize = t.TryGetProperty("totalSize", out var ts) ? ts.GetInt64() : 0,
                    RemainingSize = t.TryGetProperty("leftUntilDone", out var lu) ? lu.GetInt64() : 0,
                    Status = MapStatus(status),
                    OutputPath = t.TryGetProperty("downloadDir", out var dd) ? dd.GetString() : "",
                    Category = labels.Count > 0 ? labels[0] : "",
                });
            }

            _logger.Debug("Fetched {0} items from Transmission", items.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch Transmission items");
        }

        return items;
    }

    public byte[] GetTorrentFile(string infoHash)
    {
        try
        {
            var arguments = new
            {
                ids = new[] { infoHash },
                fields = new[] { "torrentFile" },
            };

            using var doc = SendRequest("torrent-get", arguments);
            var torrents = doc.RootElement
                .GetProperty("arguments")
                .GetProperty("torrents");

            foreach (var t in torrents.EnumerateArray())
            {
                if (t.TryGetProperty("torrentFile", out var tf))
                {
                    var filePath = tf.GetString();
                    if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                    {
                        return System.IO.File.ReadAllBytes(filePath);
                    }

                    _logger.Warn("Transmission torrent file path not accessible: {0}", filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get .torrent from Transmission: {0}", infoHash);
        }

        return null;
    }

    public bool AddTrackers(string infoHash, IEnumerable<string> trackers)
    {
        if (string.IsNullOrWhiteSpace(infoHash) || trackers == null)
        {
            return false;
        }

        try
        {
            var trackerArray = new List<string>(trackers).ToArray();
            var args = new
            {
                ids = new[] { infoHash },
                trackerAdd = trackerArray
            };

            using var doc = SendRequest("torrent-set", args);
            return doc.RootElement.TryGetProperty("result", out var res) && res.GetString() == "success";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add trackers to Transmission for torrent {0}", infoHash);
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
            using var doc = SendRequest("session-get", new { });
            if (doc.RootElement.TryGetProperty("result", out var result) && result.GetString() == "success")
            {
                var version = "";
                if (doc.RootElement.TryGetProperty("arguments", out var args) &&
                    args.TryGetProperty("version", out var v))
                {
                    version = v.GetString();
                }

                var verStr = string.IsNullOrEmpty(version) ? "" : $" v{version}";
                return DownloadClientTestResult.Ok($"Successfully connected to Transmission{verStr} at {RpcUrl}");
            }

            return DownloadClientTestResult.Fail("Transmission session-get returned unexpected result");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return DownloadClientTestResult.Fail("Authentication failed (HTTP 401 Unauthorized). Please check username and password.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return DownloadClientTestResult.Fail($"Endpoint not found (HTTP 404 Not Found) at {RpcUrl}. Please verify the host and port.");
        }
        catch (HttpRequestException ex)
        {
            return DownloadClientTestResult.Fail($"Network error connecting to {RpcUrl}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return DownloadClientTestResult.Fail($"Connection timed out connecting to {RpcUrl} (exceeded 10s)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Transmission connection test failed");
            return DownloadClientTestResult.Fail($"Connection failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }

    private static string MapStatus(int transmissionStatus)
    {
        return transmissionStatus switch
        {
            0 => "paused",
            1 or 2 => "checking",
            3 or 4 => "downloading",
            5 or 6 => "seeding",
            _ => "unknown",
        };
    }
}
