using System.Linq;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.HealthCheck.Checks;

public class NoIndexersCheck : IHealthCheck
{
    private readonly IIndexerFactory _indexerFactory;

    public NoIndexersCheck(IIndexerFactory indexerFactory)
    {
        _indexerFactory = indexerFactory;
    }

    public HealthCheckResult Check()
    {
        var indexers = _indexerFactory.All();
        if (!indexers.Any(i => i.Enable))
        {
            return HealthCheckResult.Notice(
                "NoIndexers",
                "No indexers configured. Add an indexer (Prowlarr, Torznab) in Settings > Indexers for search and RSS functionality.");
        }

        return HealthCheckResult.Ok("NoIndexers");
    }
}
