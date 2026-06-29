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
        try
        {
            var apiPath = string.IsNullOrEmpty(definition.ApiPath) ? "/api" : definition.ApiPath;
            var url = $"{definition.Url.TrimEnd('/')}{apiPath}?t=caps&apikey={definition.ApiKey}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = Client.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test Torznab connection at {0}", definition.Url);
            return false;
        }
    }
}
