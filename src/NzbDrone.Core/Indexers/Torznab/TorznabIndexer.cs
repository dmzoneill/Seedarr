using System;
using System.Net.Http;
using NLog;

namespace NzbDrone.Core.Indexers.Torznab;

public class TorznabIndexer : IIndexer
{
    private static readonly HttpClient Client = new();
    private readonly Logger _logger;

    public string Name => "Torznab";
    public string IndexerType => "Torznab";

    public TorznabIndexer()
    {
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool TestConnection(IndexerDefinition definition)
    {
        return TestConnectionDetailed(definition).Success;
    }

    public IndexerTestResult TestConnectionDetailed(IndexerDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Url))
        {
            return new IndexerTestResult { Success = false, Message = "URL is required." };
        }

        try
        {
            var apiPath = string.IsNullOrEmpty(definition.ApiPath) ? "/api" : definition.ApiPath;
            var url = $"{definition.Url.TrimEnd('/')}{apiPath}?t=caps";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(definition.ApiKey))
            {
                request.Headers.Add("X-Api-Key", definition.ApiKey);
            }

            using var response = Client.Send(request);

            if (response.IsSuccessStatusCode)
            {
                return new IndexerTestResult
                {
                    Success = true,
                    Message = $"Successfully connected to Torznab indexer at {definition.Url}"
                };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new IndexerTestResult
                {
                    Success = false,
                    Message = "Authentication failed: Invalid API Key."
                };
            }

            return new IndexerTestResult
            {
                Success = false,
                Message = $"Torznab returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test Torznab connection at {0}", definition.Url);
            return new IndexerTestResult
            {
                Success = false,
                Message = $"Unable to connect to Torznab at {definition.Url}: {ex.Message}"
            };
        }
    }

    public byte[] FetchTorrentByHash(IndexerDefinition definition, string infoHash)
    {
        try
        {
            var apiPath = string.IsNullOrEmpty(definition.ApiPath) ? "/api" : definition.ApiPath;
            var url = $"{definition.Url.TrimEnd('/')}{apiPath}?t=search&infohash={infoHash}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", definition.ApiKey);
            using var response = Client.Send(request);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);

            var enclosure = doc.SelectSingleNode("//item/enclosure");
            if (enclosure != null && enclosure.Attributes["url"] != null)
            {
                var downloadUrl = enclosure.Attributes["url"].Value;

                using var dlRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                using var dlResponse = Client.Send(dlRequest);
                if (dlResponse.IsSuccessStatusCode)
                {
                    return dlResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch torrent by hash from Torznab at {0}", definition.Url);
            return null;
        }
    }
}
