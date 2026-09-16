using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.ArrIntegration.Webhook;
using Seedarr.Api.V1.ArrIntegration;

namespace NzbDrone.Core.Test.ArrIntegration;

[TestFixture]
public class ArrConnectionControllerTest
{
    private IArrConnectionFactory _connectionFactory;
    private IArrSyncService _arrSyncService;
    private IArrWebhookRegistration _webhookRegistration;
    private ArrConnectionController _controller;

    [SetUp]
    public void SetUp()
    {
        _connectionFactory = Substitute.For<IArrConnectionFactory>();
        _arrSyncService = Substitute.For<IArrSyncService>();
        _webhookRegistration = Substitute.For<IArrWebhookRegistration>();

        _controller = new ArrConnectionController(
            _connectionFactory,
            _arrSyncService,
            _webhookRegistration);
    }

    [Test]
    public void Update_when_webhook_disabled_triggers_unregister_webhook_with_existing()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = true
        };
        _connectionFactory.Get(1).Returns(existing);

        var updated = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = false
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _webhookRegistration.Received(1).UnregisterWebhook(existing);
        _webhookRegistration.DidNotReceive().RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
        _connectionFactory.Received(1).Update(updated);
    }

    [Test]
    public void Update_when_connection_disabled_triggers_unregister_webhook_with_existing()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Radarr",
            ArrType = "Radarr",
            Url = "http://radarr:7878",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = true
        };
        _connectionFactory.Get(1).Returns(existing);

        var updated = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Radarr",
            ArrType = "Radarr",
            Url = "http://radarr:7878",
            ApiKey = "api-key",
            Enable = false,
            WebhookEnabled = true
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _webhookRegistration.Received(1).UnregisterWebhook(existing);
        _webhookRegistration.DidNotReceive().RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
        _connectionFactory.Received(1).Update(updated);
    }

    [Test]
    public void Update_when_url_changed_triggers_unregister_webhook_with_existing_and_registers_new()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = true
        };
        _connectionFactory.Get(1).Returns(existing);

        var updated = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://new-sonarr:8989",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = true
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _webhookRegistration.Received(1).UnregisterWebhook(existing);
        _webhookRegistration.Received(1).RegisterWebhook(Arg.Is<ArrConnectionDefinition>(d => d.Url == "http://new-sonarr:8989"));
        _connectionFactory.Received(1).Update(updated);
    }

    [Test]
    public void Update_when_url_has_matching_trimmed_slashes_does_not_unregister()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989/",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = true
        };
        _connectionFactory.Get(1).Returns(existing);

        var updated = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = true
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _webhookRegistration.DidNotReceive().UnregisterWebhook(Arg.Any<ArrConnectionDefinition>());
        _webhookRegistration.Received(1).RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
        _connectionFactory.Received(1).Update(updated);
    }

    [Test]
    public void Update_when_connection_not_found_returns_not_found()
    {
        _connectionFactory.Get(99).Returns((ArrConnectionDefinition)null);

        var updated = new ArrConnectionDefinition
        {
            Id = 99,
            Name = "Nonexistent",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989"
        };

        var result = _controller.Update(99, updated);

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
        _webhookRegistration.DidNotReceive().UnregisterWebhook(Arg.Any<ArrConnectionDefinition>());
        _webhookRegistration.DidNotReceive().RegisterWebhook(Arg.Any<ArrConnectionDefinition>());
    }

    [Test]
    public void Delete_triggers_unregister_webhook_and_deletes_connection()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "api-key"
        };
        _connectionFactory.Get(1).Returns(existing);

        var result = _controller.Delete(1);

        Assert.That(result, Is.InstanceOf<OkResult>());
        _webhookRegistration.Received(1).UnregisterWebhook(existing);
        _connectionFactory.Received(1).Delete(1);
    }
}
