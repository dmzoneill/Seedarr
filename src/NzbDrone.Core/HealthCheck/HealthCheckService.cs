using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    System.Threading.Tasks.Task<List<HealthCheckResult>> PerformChecksAsync(System.Threading.CancellationToken cancellationToken = default);
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
    private readonly SemaphoreSlim _executionLock = new(1, 1);
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
        return PerformChecksAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<List<HealthCheckResult>> PerformChecksAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_cacheLock)
        {
            if (_cachedResults != null && DateTime.UtcNow - _lastRunTimeUtc < _cacheDuration)
            {
                return _cachedResults.ToList();
            }
        }

        await _executionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_cacheLock)
            {
                if (_cachedResults != null && DateTime.UtcNow - _lastRunTimeUtc < _cacheDuration)
                {
                    return _cachedResults.ToList();
                }
            }

            var results = new List<HealthCheckResult>();
            if (_healthChecks != null)
            {
                foreach (var check in _healthChecks)
                {
                    if (check == null)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var checkName = check.GetType().Name;
                    try
                    {
                        var task = Task.Run(() => check.Check());
                        task.ContinueWith(
                            t => _logger.Debug(t.Exception, "Health check {0} faulted in background", checkName),
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

                        var delayTask = Task.Delay(_checkTimeout, cancellationToken);
                        var completedTask = await Task.WhenAny(task, delayTask).ConfigureAwait(false);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        if (completedTask == task)
                        {
                            var checkResult = await task.ConfigureAwait(false);
                            if (checkResult != null)
                            {
                                results.Add(checkResult);
                            }
                            else
                            {
                                _logger.Warn("Health check {0} returned a null result", checkName);
                                results.Add(HealthCheckResult.Error(checkName, "Health check returned null result"));
                            }
                        }
                        else
                        {
                            results.Add(HealthCheckResult.Error(checkName, "Health check timed out"));
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        _logger.Debug("Health checks canceled");
                        throw;
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
            }

            foreach (var result in results)
            {
                if (result == null)
                {
                    continue;
                }

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

            lock (_cacheLock)
            {
                _cachedResults = results.ToList();
                _lastRunTimeUtc = DateTime.UtcNow;
            }

            _signalRBroadcaster?.BroadcastMessage(new SignalRMessage
            {
                Name = "HealthCheckCompleted",
                Action = ModelAction.Updated,
                Body = _cachedResults
            });

            return results;
        }
        finally
        {
            _executionLock.Release();
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
