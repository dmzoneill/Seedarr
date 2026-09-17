using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.HealthCheck;

public interface IHealthCheckService
{
    List<HealthCheckResult> PerformChecks();
}

public class HealthCheckService : IHealthCheckService
{
    private readonly IEnumerable<IHealthCheck> _healthChecks;
    private readonly IEventAggregator _eventAggregator;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, HealthCheckResultType> _previousResults = new(StringComparer.OrdinalIgnoreCase);

    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks)
        : this(healthChecks, null)
    {
    }

    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks, IEventAggregator eventAggregator)
    {
        _healthChecks = healthChecks;
        _eventAggregator = eventAggregator;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public List<HealthCheckResult> PerformChecks()
    {
        var results = new List<HealthCheckResult>();
        foreach (var check in _healthChecks)
        {
            try
            {
                var result = check.Check();
                if (result.Type != HealthCheckResultType.Ok)
                {
                    _logger.Warn("Health check {0}: {1}", result.Source, result.Message);
                }

                results.Add(result);
            }
            catch (Exception ex)
            {
                var checkName = check.GetType().Name;
                _logger.Error(ex, "Health check {0} threw an unhandled exception", checkName);
                results.Add(HealthCheckResult.Error(checkName, $"Health check failed with exception: {ex.Message}"));
            }
        }

        foreach (var result in results)
        {
            var source = result.Source ?? string.Empty;
            var isDegraded = IsDegraded(result.Type);

            if (_previousResults.TryGetValue(source, out var previousType))
            {
                var wasDegraded = IsDegraded(previousType);

                if (!wasDegraded && isDegraded)
                {
                    _eventAggregator?.PublishEvent(new HealthIssueEvent((Torrent)null, result.Source, result.Message, isResolved: false));
                }
                else if (wasDegraded && !isDegraded)
                {
                    _eventAggregator?.PublishEvent(new HealthIssueEvent((Torrent)null, result.Source, result.Message, isResolved: true));
                }
            }
            else
            {
                if (isDegraded)
                {
                    _eventAggregator?.PublishEvent(new HealthIssueEvent((Torrent)null, result.Source, result.Message, isResolved: false));
                }
            }

            _previousResults[source] = result.Type;
        }

        return results;
    }

    private static bool IsDegraded(HealthCheckResultType type) =>
        type is HealthCheckResultType.Warning or HealthCheckResultType.Error;
}
