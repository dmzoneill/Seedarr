using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.ArrIntegration;

public class SonarrConnection : IArrConnection
{
    private static readonly HttpClient Client = new();
    private readonly Logger _logger;

    public string Name => "Sonarr";
    public string ArrType => "Sonarr";

    public string Url { get; set; } = "http://localhost:8989";
    public string ApiKey { get; set; } = "";

    public SonarrConnection()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<ArrDownloadRecord> GetDownloadHistory()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{Url}/api/v3/history?pageSize=50&sortKey=date&sortDirection=descending");
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = Client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn("Sonarr API returned {0}", response.StatusCode);
                return new List<ArrDownloadRecord>();
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
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

            _logger.Debug("Fetched {0} download records from Sonarr", records.Count);
            return records;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch Sonarr history");
            return new List<ArrDownloadRecord>();
        }
    }

    public bool TestConnection()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{Url}/api/v3/system/status");
            request.Headers.Add("X-Api-Key", ApiKey);
            var response = Client.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Sonarr connection test failed");
            return false;
        }
    }
}
