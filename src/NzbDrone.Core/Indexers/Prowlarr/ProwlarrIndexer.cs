using System;
using System.Net.Http;
using NLog;

namespace NzbDrone.Core.Indexers.Prowlarr;

public class ProwlarrIndexer : IIndexer
{
    private static readonly HttpClient Client = new();
    private readonly Logger _logger;

    public string Name => "Prowlarr";
    public string IndexerType => "Prowlarr";

    public ProwlarrIndexer()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool TestConnection(IndexerDefinition definition)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{definition.Url.TrimEnd('/')}/api/v1/health");
            request.Headers.Add("X-Api-Key", definition.ApiKey);

            using var response = Client.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test Prowlarr connection at {0}", definition.Url);
            return false;
        }
    }

    public byte[] FetchTorrentByHash(IndexerDefinition definition, string infoHash)
    {
        try
        {
            var url = $"{definition.Url.TrimEnd('/')}/api/v1/search?query={infoHash}&type=search";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", definition.ApiKey);
            using var response = Client.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = System.Text.Json.JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("downloadUrl", out var downloadUrlProp) && downloadUrlProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var downloadUrl = downloadUrlProp.GetString();
                        if (!string.IsNullOrEmpty(downloadUrl))
                        {
                            using var dlRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                            using var dlResponse = Client.Send(dlRequest);
                            if (dlResponse.IsSuccessStatusCode)
                            {
                                return dlResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                            }
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch torrent by hash from Prowlarr at {0}", definition.Url);
            return null;
        }
    }
}
