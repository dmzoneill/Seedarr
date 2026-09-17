using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.ArrIntegration.Webhook;
using NzbDrone.Core.Lifecycle;

namespace NzbDrone.Core.Test.ArrIntegration.Webhook;

[TestFixture]
public class ArrWebhookMaintenanceTaskTest
{
    private IArrConnectionFactory _connectionFactory;
    private IArrWebhookRegistration _webhookRegistration;
    private ArrWebhookMaintenanceTask _task;

    [SetUp]
    public void Setup()
    {
        _connectionFactory = Substitute.For<IArrConnectionFactory>();
        _webhookRegistration = Substitute.For<IArrWebhookRegistration>();
        _task = new ArrWebhookMaintenanceTask(_connectionFactory, _webhookRegistration);
    }

    [Test]
    public void Execute_should_register_webhooks_for_enabled_connections()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, WebhookEnabled = true, ArrType = "Sonarr", Url = "http://localhost:8989" },
            new() { Enable = true, WebhookEnabled = true, ArrType = "Radarr", Url = "http://localhost:7878" }
        };
        _connectionFactory.All().Returns(connections);
        _webhookRegistration.RegisterWebhook(Arg.Any<ArrConnectionDefinition>()).Returns(true);

        _task.Execute();

        _webhookRegistration.Received(2).RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
    }

    [Test]
    public void Execute_should_skip_disabled_connections()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Enable = false, WebhookEnabled = true, ArrType = "Sonarr" },
            new() { Enable = true, WebhookEnabled = true, ArrType = "Radarr", Url = "http://localhost:7878" }
        };
        _connectionFactory.All().Returns(connections);
        _webhookRegistration.RegisterWebhook(Arg.Any<ArrConnectionDefinition>()).Returns(true);

        _task.Execute();

        _webhookRegistration.Received(1).RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
    }

    [Test]
    public void Execute_should_skip_connections_with_webhook_disabled()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, WebhookEnabled = false, ArrType = "Sonarr" },
            new() { Enable = true, WebhookEnabled = true, ArrType = "Radarr", Url = "http://localhost:7878" }
        };
        _connectionFactory.All().Returns(connections);
        _webhookRegistration.RegisterWebhook(Arg.Any<ArrConnectionDefinition>()).Returns(true);

        _task.Execute();

        _webhookRegistration.Received(1).RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
    }

    [Test]
    public void Execute_should_not_call_register_when_no_connections()
    {
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition>());

        _task.Execute();

        _webhookRegistration.DidNotReceive().RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
    }

    [Test]
    public void Execute_should_continue_when_one_registration_fails()
    {
        var sonarr = new ArrConnectionDefinition { Enable = true, WebhookEnabled = true, ArrType = "Sonarr", Url = "http://localhost:8989" };
        var radarr = new ArrConnectionDefinition { Enable = true, WebhookEnabled = true, ArrType = "Radarr", Url = "http://localhost:7878" };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { sonarr, radarr });
        _webhookRegistration.RegisterWebhook(sonarr).Returns(false);
        _webhookRegistration.RegisterWebhook(radarr).Returns(true);

        _task.Execute();

        _webhookRegistration.Received(1).RegisterWebhook(sonarr);
        _webhookRegistration.Received(1).RegisterWebhook(radarr);
    }

    [Test]
    public void Handle_ApplicationStartedEvent_should_register_webhooks()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, WebhookEnabled = true, ArrType = "Sonarr", Url = "http://localhost:8989" }
        };
        _connectionFactory.All().Returns(connections);
        _webhookRegistration.RegisterWebhook(Arg.Any<ArrConnectionDefinition>()).Returns(true);

        _task.Handle(new ApplicationStartedEvent());
        _task.LastStartupTask?.Wait();

        _webhookRegistration.Received(1).RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
    }

    [Test]
    public void Handle_ApplicationStartedEvent_should_be_non_blocking()
    {
        var connections = new List<ArrConnectionDefinition>
        {
            new() { Enable = true, WebhookEnabled = true, ArrType = "Sonarr", Url = "http://localhost:8989" }
        };
        _connectionFactory.All().Returns(connections);

        var blockingTcs = new TaskCompletionSource<bool>();
        _webhookRegistration.RegisterWebhook(Arg.Any<ArrConnectionDefinition>()).Returns(x =>
        {
            blockingTcs.Task.Wait(5000);
            return true;
        });

        // Handle should return immediately before RegisterWebhook completes
        _task.Handle(new ApplicationStartedEvent());
        Assert.That(_task.LastStartupTask, Is.Not.Null);
        Assert.That(_task.LastStartupTask.IsCompleted, Is.False);

        // Unblock registration and wait for background task to complete
        blockingTcs.SetResult(true);
        _task.LastStartupTask.Wait(5000);

        _webhookRegistration.Received(1).RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
    }

    [Test]
    public void Handle_ApplicationStartedEvent_should_retry_failed_registrations()
    {
        var connection = new ArrConnectionDefinition { Enable = true, WebhookEnabled = true, ArrType = "Sonarr", Url = "http://localhost:8989" };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { connection });

        // First attempt fails, second attempt (retry 1) succeeds
        _webhookRegistration.RegisterWebhook(connection).Returns(false, true);

        _task.DelayAsync = _ => Task.CompletedTask;
        _task.RetryDelays = new[] { TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2) };

        _task.Handle(new ApplicationStartedEvent());
        _task.LastStartupTask?.Wait(5000);

        _webhookRegistration.Received(2).RegisterWebhook(connection);
    }

    [Test]
    public void Handle_ApplicationStartedEvent_should_stop_retrying_when_all_succeed()
    {
        var connection = new ArrConnectionDefinition { Enable = true, WebhookEnabled = true, ArrType = "Sonarr", Url = "http://localhost:8989" };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { connection });

        // Succeeds on initial attempt
        _webhookRegistration.RegisterWebhook(connection).Returns(true);

        _task.DelayAsync = _ => Task.CompletedTask;
        _task.RetryDelays = new[] { TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(3) };

        _task.Handle(new ApplicationStartedEvent());
        _task.LastStartupTask?.Wait(5000);

        _webhookRegistration.Received(1).RegisterWebhook(connection);
    }

    [Test]
    public void DefaultInterval_should_be_360_minutes()
    {
        Assert.That(_task.DefaultInterval, Is.EqualTo(360));
    }

    [Test]
    public void Execute_should_handle_exception_from_registration_and_continue()
    {
        var sonarr = new ArrConnectionDefinition { Enable = true, WebhookEnabled = true, ArrType = "Sonarr", Url = "http://localhost:8989" };
        var radarr = new ArrConnectionDefinition { Enable = true, WebhookEnabled = true, ArrType = "Radarr", Url = "http://localhost:7878" };
        _connectionFactory.All().Returns(new List<ArrConnectionDefinition> { sonarr, radarr });
        _webhookRegistration.RegisterWebhook(sonarr).Returns(x => throw new System.Net.Http.HttpRequestException("connection refused"));
        _webhookRegistration.RegisterWebhook(radarr).Returns(true);

        Assert.DoesNotThrow(() => _task.Execute());
        _webhookRegistration.Received(1).RegisterWebhook(radarr);
    }
}
