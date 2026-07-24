using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Http;
using Polly;

namespace NzbDrone.Core.ArrIntegration;

public class RadarrConnection : IArrConnection
{
    private static readonly HttpClient Client = new();
    private static readonly ResiliencePipeline Policy = ResiliencePolicies.GetArrApiPolicy();

    private readonly Logger _logger;

    public string Name => "Radarr";
    public string ArrType => "Radarr";

    public string Url { get; set; } = "http://localhost:7878";
    public string ApiKey { get; set; } = "";

    public RadarrConnection()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<ArrDownloadRecord> GetDownloadHistory()
    {
        try
        {
            var result = Policy.Execute(ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{Url}/api/v3/history?pageSize=50&sortKey=date&sortDirection=descending");
                request.Headers.Add("X-Api-Key", ApiKey);

                var response = Client.Send(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("Radarr API returned {0}", response.StatusCode);
                    return (string)null;
                }

                return response.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            });

            if (result == null)
            {
                return new List<ArrDownloadRecord>();
            }

            var json = result;
            var doc = JsonDocument.Parse(json);
            var records = new List<ArrDownloadRecord>();

            if (doc.RootElement.TryGetProperty("records", out var recordsArray))
            {
                foreach (var record in recordsArray.EnumerateArray())
                {
                    var eventType = record.GetProperty("eventType").GetString();
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

    public bool TestConnection()
    {
        try
        {
            return Policy.Execute(ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{Url}/api/v3/system/status");
                request.Headers.Add("X-Api-Key", ApiKey);
                var response = Client.Send(request, ct);
                return response.IsSuccessStatusCode;
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Radarr connection test failed");
            return false;
        }
    }
}
