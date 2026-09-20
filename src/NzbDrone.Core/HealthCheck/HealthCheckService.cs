using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;
using NzbDrone.SignalR;

namespace NzbDrone.Core.HealthCheck;

public interface IHealthCheckService
{
    List<HealthCheckResult> PerformChecks();
    void ExpireCache();
}

public class HealthCheckService : IHealthCheckService
{
    private readonly IEnumerable<IHealthCheck> _healthChecks;
    private readonly IEventAggregator _eventAggregator;
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;
    private readonly Logger _logger;
    private readonly ConcurrentDictionary<string, HealthCheckResultType> _previousResults = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _cacheLock = new();
    private List<HealthCheckResult> _cachedResults;
    private DateTime _lastRunTimeUtc = DateTime.MinValue;
    private TimeSpan _cacheDuration = TimeSpan.FromSeconds(15);
    private TimeSpan _checkTimeout = TimeSpan.FromSeconds(5);

    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks)
        : this(healthChecks, null, null)
    {
    }

    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks, IEventAggregator eventAggregator)
        : this(healthChecks, eventAggregator, null)
    {
    }

    public HealthCheckService(IEnumerable<IHealthCheck> healthChecks, IEventAggregator eventAggregator, IBroadcastSignalRMessage signalRBroadcaster)
    {
        _healthChecks = healthChecks;
        _eventAggregator = eventAggregator;
        _signalRBroadcaster = signalRBroadcaster;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public TimeSpan CacheDuration
    {
        get => _cacheDuration;
        set => _cacheDuration = value;
    }

    public TimeSpan CheckTimeout
    {
        get => _checkTimeout;
        set => _checkTimeout = value;
    }

    public void ExpireCache()
    {
        lock (_cacheLock)
        {
            _lastRunTimeUtc = DateTime.MinValue;
            _cachedResults = null;
        }
    }

    public List<HealthCheckResult> PerformChecks()
    {
        lock (_cacheLock)
        {
            if (_cachedResults != null && DateTime.UtcNow - _lastRunTimeUtc < _cacheDuration)
            {
                return _cachedResults.ToList();
            }

            var results = new List<HealthCheckResult>();
            foreach (var check in _healthChecks)
            {
                var checkName = check.GetType().Name;
                try
                {
                    var task = Task.Run(() => check.Check());
                    if (task.Wait(_checkTimeout))
                    {
                        results.Add(task.Result);
                    }
                    else
                    {
                        results.Add(HealthCheckResult.Error(checkName, "Health check timed out"));
                    }
                }
                catch (AggregateException aex)
                {
                    var inner = aex.InnerExceptions.Count == 1 ? aex.InnerExceptions[0] : aex;
                    _logger.Debug(inner, "Health check {0} threw an exception", checkName);
                    results.Add(HealthCheckResult.Error(checkName, $"Health check failed with exception: {inner.Message}"));
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Health check {0} threw an unhandled exception", checkName);
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

                    if (previousType != result.Type)
                    {
                        LogStatus(result);
                    }

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
                    LogStatus(result);

                    if (isDegraded)
                    {
                        _eventAggregator?.PublishEvent(new HealthIssueEvent((Torrent)null, result.Source, result.Message, isResolved: false));
                    }
                }

                _previousResults[source] = result.Type;
            }

            _cachedResults = results.ToList();
            _lastRunTimeUtc = DateTime.UtcNow;

            _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
            {
                Name = "HealthCheckCompleted",
                Action = ModelAction.Updated,
                Body = _cachedResults
            });

            return results;
        }
    }

    private void LogStatus(HealthCheckResult result)
    {
        if (result.Type == HealthCheckResultType.Warning)
        {
            _logger.Warn("Health check {0}: {1}", result.Source, result.Message);
        }
        else if (result.Type == HealthCheckResultType.Error)
        {
            _logger.Error("Health check {0}: {1}", result.Source, result.Message);
        }
    }

    private static bool IsDegraded(HealthCheckResultType type) =>
        type is HealthCheckResultType.Warning or HealthCheckResultType.Error;
}
