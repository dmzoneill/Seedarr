using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ArrIntegration.Webhook;

public class ArrWebhookMaintenanceTask : IScheduledTask, IHandle<ApplicationStartedEvent>
{
    public static readonly TimeSpan[] DefaultRetryDelays =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)
    };

    private readonly IArrConnectionFactory _connectionFactory;
    private readonly IArrWebhookRegistration _webhookRegistration;
    private readonly Logger _logger;

    public int DefaultInterval => 360;

    public TimeSpan[] RetryDelays { get; set; } = DefaultRetryDelays;

    public Task LastStartupTask { get; internal set; }

    internal Func<TimeSpan, Task> DelayAsync = Task.Delay;

    public ArrWebhookMaintenanceTask(
        IArrConnectionFactory connectionFactory,
        IArrWebhookRegistration webhookRegistration)
    {
        _connectionFactory = connectionFactory;
        _webhookRegistration = webhookRegistration;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public void Execute()
    {
        var failed = RegisterAllWebhooks();
        if (failed.Count > 0)
        {
            _ = Task.Run(async () => await RetryFailedConnectionsAsync(failed));
        }
    }

    public void Handle(ApplicationStartedEvent message)
    {
        LastStartupTask = Task.Run(async () =>
        {
            var failed = RegisterAllWebhooks();
            if (failed.Count > 0)
            {
                await RetryFailedConnectionsAsync(failed);
            }
        });
    }

    private List<ArrConnectionDefinition> RegisterAllWebhooks()
    {
        var connections = _connectionFactory.All()
            .Where(c => c.Enable && c.WebhookEnabled)
            .ToList();

        if (connections.Count == 0)
        {
            return new List<ArrConnectionDefinition>();
        }

        _logger.Info("Webhook maintenance: checking {0} connection(s)", connections.Count);

        var registered = 0;
        var failed = 0;
        var failedList = new List<ArrConnectionDefinition>();

        foreach (var connection in connections)
        {
            try
            {
                if (_webhookRegistration.RegisterWebhook(connection))
                {
                    registered++;
                }
                else
                {
                    failed++;
                    failedList.Add(connection);
                    _logger.Warn("Webhook maintenance: failed to register webhook in {0} at {1}", connection.ArrType, connection.Url);
                }
            }
            catch (Exception ex)
            {
                failed++;
                failedList.Add(connection);
                _logger.Error(ex, "Webhook maintenance: error registering webhook in {0}", connection.ArrType);
            }
        }

        _logger.Info("Webhook maintenance complete: {0} registered, {1} failed", registered, failed);
        return failedList;
    }

    private async Task RetryFailedConnectionsAsync(List<ArrConnectionDefinition> failedConnections)
    {
        var currentFailed = failedConnections;
        foreach (var delay in RetryDelays)
        {
            try
            {
                await DelayAsync(delay);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Webhook retry delay interrupted");
                break;
            }

            _logger.Info("Webhook maintenance retry: re-checking {0} failed connection(s)", currentFailed.Count);

            var nextFailed = new List<ArrConnectionDefinition>();
            foreach (var connection in currentFailed)
            {
                try
                {
                    if (_webhookRegistration.RegisterWebhook(connection))
                    {
                        _logger.Info("Webhook registered on retry for {0} at {1}", connection.ArrType, connection.Url);
                    }
                    else
                    {
                        nextFailed.Add(connection);
                        _logger.Warn("Webhook maintenance retry: still failing for {0} at {1}", connection.ArrType, connection.Url);
                    }
                }
                catch (Exception ex)
                {
                    nextFailed.Add(connection);
                    _logger.Error(ex, "Webhook maintenance retry: error registering webhook in {0}", connection.ArrType);
                }
            }

            currentFailed = nextFailed;
            if (currentFailed.Count == 0)
            {
                _logger.Info("Webhook maintenance retry: all connections registered successfully");
                break;
            }
        }
    }
}
