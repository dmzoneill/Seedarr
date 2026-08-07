using System;
using System.Net.Http;
using NLog;

namespace NzbDrone.Core.Indexers.Newznab;

public class NewznabIndexer : IIndexer
{
    private static readonly HttpClient Client = new();
    private readonly Logger _logger;

    public string Name => "Newznab";
    public string IndexerType => "Newznab";

    public NewznabIndexer()
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
                    Message = $"Successfully connected to Newznab indexer at {definition.Url}"
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
                Message = $"Newznab returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test Newznab connection at {0}", definition.Url);
            return new IndexerTestResult
            {
                Success = false,
                Message = $"Unable to connect to Newznab at {definition.Url}: {ex.Message}"
            };
        }
    }

    public byte[] FetchTorrentByHash(IndexerDefinition definition, string infoHash)
    {
        // Newznab is typically Usenet and doesn't support infohash searches,
        // so we just return null to fallback/skip.
        return null;
    }
}
