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
        try
        {
            var apiPath = string.IsNullOrEmpty(definition.ApiPath) ? "/api" : definition.ApiPath;
            var url = $"{definition.Url.TrimEnd('/')}{apiPath}?t=caps";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", definition.ApiKey);
            using var response = Client.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test Newznab connection at {0}", definition.Url);
            return false;
        }
    }
}
