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
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{definition.Url.TrimEnd('/')}/api/v1/health");
            request.Headers.Add("X-Api-Key", definition.ApiKey);

            var response = Client.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to test Prowlarr connection at {0}", definition.Url);
            return false;
        }
    }
}
