using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using Polly;

namespace NzbDrone.Core.ArrIntegration;

public class RadarrConnection : IArrConnection
{
    private readonly HttpClient _client;
    private readonly ResiliencePipeline _policy;
    private readonly Logger _logger;

    public string Name => "Radarr";
    public string ArrType => "Radarr";

    public string Url { get; set; } = "http://localhost:7878";
    public string ApiKey { get; set; } = "";

    public RadarrConnection(HttpClient client = null, ResiliencePipeline policy = null)
    {
        _logger = LogManager.GetCurrentClassLogger();
        _client = client ?? ArrConnectionResources.SharedClient;
        _policy = policy ?? ArrConnectionResources.SharedPolicy;
    }

    public List<ArrDownloadRecord> GetDownloadHistory()
    {
        try
        {
            var result = _policy.Execute(ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url}/api/v3/history?pageSize=50&sortKey=date&sortDirection=descending");
                request.Headers.Add("X-Api-Key", ApiKey);

                using var response = _client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("Radarr API returned {0}", response.StatusCode);
                    return (string)null;
                }

                using var stream = response.Content.ReadAsStream(ct);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            });

            if (result == null)
            {
                return new List<ArrDownloadRecord>();
            }

            var json = result;
            using var doc = JsonDocument.Parse(json);
            var records = new List<ArrDownloadRecord>();

            if (doc.RootElement.TryGetProperty("records", out var recordsArray))
            {
                foreach (var record in recordsArray.EnumerateArray())
                {
                    if (!record.TryGetProperty("eventType", out var eventTypeElement))
                    {
                        continue;
                    }

                    var eventType = eventTypeElement.GetString();
                    if (eventType != "grabbed")
                    {
                        continue;
                    }

                    var downloadRecord = new ArrDownloadRecord
                    {
                        Title = record.TryGetProperty("sourceTitle", out var title) ? title.GetString() : "",
                        DownloadId = record.TryGetProperty("downloadId", out var dlId) ? dlId.GetString() : "",
                        Date = record.TryGetProperty("date", out var date) ? date.GetDateTime() : DateTime.UtcNow
                    };

                    if (record.TryGetProperty("data", out var data))
                    {
                        downloadRecord.InfoHash = data.TryGetProperty("torrentInfoHash", out var hash) ? hash.GetString() : null;
                        downloadRecord.Indexer = data.TryGetProperty("indexer", out var indexer) ? indexer.GetString() : null;
                        downloadRecord.DownloadClient = data.TryGetProperty("downloadClient", out var dc) ? dc.GetString() : null;
                        downloadRecord.DownloadUrl = data.TryGetProperty("downloadUrl", out var dlUrl) ? dlUrl.GetString() : null;
                    }

                    if (!string.IsNullOrEmpty(downloadRecord.InfoHash))
                    {
                        records.Add(downloadRecord);
                    }
                }
            }

            _logger.Debug("Fetched {0} download records from Radarr", records.Count);
            return records;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch Radarr history");
            return new List<ArrDownloadRecord>();
        }
    }

    public bool TestConnection() => TestConnectionDetailed().Success;

    public ArrTestResult TestConnectionDetailed()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            return ArrTestResult.Fail("URL cannot be empty");
        }

        try
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Url.TrimEnd('/')}/api/v3/system/status");
            request.Headers.Add("X-Api-Key", ApiKey ?? "");
            using var response = _client.Send(request, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                return ArrTestResult.Ok($"Successfully connected to Radarr at {Url}");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return ArrTestResult.Fail("Authentication failed (HTTP 401 Unauthorized). Please check your API key.");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ArrTestResult.Fail($"Endpoint not found (HTTP 404 Not Found) at {Url}. Verify the URL and port.");
            }

            return ArrTestResult.Fail($"Radarr returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "Radarr connection test failed: {0}", ex.Message);
            return ArrTestResult.Fail($"Network error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            _logger.Error("Radarr connection test timed out");
            return ArrTestResult.Fail($"Connection timed out connecting to {Url} (exceeded 10s)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Radarr connection test failed");
            return ArrTestResult.Fail($"Connection failed: {ex.Message}");
        }
    }
}
