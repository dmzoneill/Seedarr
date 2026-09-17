using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.HealthCheck.Checks;

public class IndexerHealthCheck : IHealthCheck
{
    private readonly IIndexerFactory _indexerFactory;
    private readonly IIndexerStatusService _indexerStatusService;

    public IndexerHealthCheck(IIndexerFactory indexerFactory, IIndexerStatusService indexerStatusService)
    {
        _indexerFactory = indexerFactory;
        _indexerStatusService = indexerStatusService;
    }

    public HealthCheckResult Check()
    {
        var enabledIndexers = _indexerFactory.All().Where(i => i.Enable).ToList();
        if (enabledIndexers.Count == 0)
        {
            return HealthCheckResult.Ok("IndexerHealth");
        }

        var authFailed = new List<string>();
        var rateLimited = new List<string>();
        var disabled = new List<string>();

        foreach (var indexer in enabledIndexers)
        {
            if (_indexerStatusService.IsAuthFailed(indexer.Id))
            {
                authFailed.Add(indexer.Name ?? $"Indexer {indexer.Id}");
            }
            else if (_indexerStatusService.IsRateLimited(indexer.Id))
            {
                rateLimited.Add(indexer.Name ?? $"Indexer {indexer.Id}");
            }
            else if (_indexerStatusService.IsDisabled(indexer.Id))
            {
                disabled.Add(indexer.Name ?? $"Indexer {indexer.Id}");
            }
        }

        if (authFailed.Count > 0)
        {
            return HealthCheckResult.Error(
                "IndexerHealth",
                $"Authentication failed for indexer(s): {string.Join(", ", authFailed)}. Please verify API credentials.");
        }

        if (rateLimited.Count > 0)
        {
            return HealthCheckResult.Warning(
                "IndexerHealth",
                $"Indexer(s) currently rate limited (HTTP 429): {string.Join(", ", rateLimited)}. Backoff applied.");
        }

        if (disabled.Count > 0)
        {
            return HealthCheckResult.Warning(
                "IndexerHealth",
                $"Indexer(s) temporarily disabled due to failures: {string.Join(", ", disabled)}.");
        }

        return HealthCheckResult.Ok("IndexerHealth");
    }
}
