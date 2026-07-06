using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ArrIntegration.Webhook;

public class ArrWebhookMaintenanceTask : IScheduledTask, IHandle<ApplicationStartedEvent>
{
    private readonly IArrConnectionFactory _connectionFactory;
    private readonly IArrWebhookRegistration _webhookRegistration;
    private readonly Logger _logger;

    public int DefaultInterval => 360;

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
        RegisterAllWebhooks();
    }

    public void Handle(ApplicationStartedEvent message)
    {
        RegisterAllWebhooks();
    }

    private void RegisterAllWebhooks()
    {
        var connections = _connectionFactory.All()
            .Where(c => c.Enable && c.WebhookEnabled)
            .ToList();

        if (connections.Count == 0)
        {
            return;
        }

        _logger.Info("Webhook maintenance: checking {0} connection(s)", connections.Count);

        var registered = 0;
        var failed = 0;

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
                    _logger.Warn("Webhook maintenance: failed to register webhook in {0} at {1}", connection.ArrType, connection.Url);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.Error(ex, "Webhook maintenance: error registering webhook in {0}", connection.ArrType);
            }
        }

        _logger.Info("Webhook maintenance complete: {0} registered, {1} failed", registered, failed);
    }
}
