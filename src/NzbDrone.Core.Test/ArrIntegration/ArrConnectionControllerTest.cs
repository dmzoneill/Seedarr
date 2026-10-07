using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.ArrIntegration.Webhook;
using NzbDrone.Core.Test.TestHelpers;
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
    public void Update_when_arr_type_changed_triggers_unregister_webhook_with_existing_and_registers_new()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://arr:8989",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = true
        };
        _connectionFactory.Get(1).Returns(existing);

        var updated = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Lidarr",
            Url = "http://arr:8989",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = true
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _webhookRegistration.Received(1).UnregisterWebhook(existing);
        _webhookRegistration.Received(1).RegisterWebhook(Arg.Is<ArrConnectionDefinition>(d => d.ArrType == "Lidarr"));
        _connectionFactory.Received(1).Update(updated);
    }

    [Test]
    public void Update_when_arr_type_changed_and_implementation_omitted_recomputes_implementation_from_new_type()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Implementation = "SonarrConnection",
            Url = "http://sonarr:8989",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = false
        };
        _connectionFactory.Get(1).Returns(existing);

        var updated = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Radarr",
            ArrType = "Radarr",
            Url = "http://radarr:7878",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = false
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _connectionFactory.Received(1).Update(Arg.Is<ArrConnectionDefinition>(d =>
            d.ArrType == "Radarr" && d.Implementation == "RadarrConnection"));
    }

    [Test]
    public void Update_when_arr_type_unchanged_and_implementation_omitted_preserves_existing_implementation()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Implementation = "SonarrConnection",
            Url = "http://sonarr:8989",
            ApiKey = "api-key",
            Enable = true,
            WebhookEnabled = false
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
            WebhookEnabled = false
        };

        var result = _controller.Update(1, updated);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _connectionFactory.Received(1).Update(Arg.Is<ArrConnectionDefinition>(d =>
            d.Implementation == "SonarrConnection"));
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
    public void TestDirect_with_existing_id_and_omitted_api_key_restores_stored_key_before_test()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "stored-secret-key"
        };
        _connectionFactory.Get(1).Returns(existing);
        _arrSyncService.TestConnectionDetailedDirect(Arg.Any<ArrConnectionDefinition>())
            .Returns(ArrTestResult.Ok("Connected"));

        var definition = new ArrConnectionDefinition
        {
            Id = 1,
            ArrType = "Sonarr",
            Url = "http://sonarr:8989"
        };

        var result = _controller.TestDirect(definition);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _arrSyncService.Received(1).TestConnectionDetailedDirect(Arg.Is<ArrConnectionDefinition>(d =>
            d.ApiKey == "stored-secret-key"));
    }

    [Test]
    public void TestDirect_with_existing_id_and_masked_api_key_restores_stored_key_before_test()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "stored-secret-key"
        };
        _connectionFactory.Get(1).Returns(existing);
        _arrSyncService.TestConnectionDetailedDirect(Arg.Any<ArrConnectionDefinition>())
            .Returns(ArrTestResult.Ok("Connected"));

        var definition = new ArrConnectionDefinition
        {
            Id = 1,
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "********key"
        };

        var result = _controller.TestDirect(definition);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        _arrSyncService.Received(1).TestConnectionDetailedDirect(Arg.Is<ArrConnectionDefinition>(d =>
            d.ApiKey == "stored-secret-key"));
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

    [Test]
    public async Task GetImageProxy_returns_bad_request_when_connection_id_invalid()
    {
        var result = await _controller.GetImageProxy(0, "/MediaCover/1/poster.jpg");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetImageProxy_returns_bad_request_when_path_is_empty()
    {
        var result = await _controller.GetImageProxy(1, "");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetImageProxy_returns_bad_request_when_path_is_not_allowlisted()
    {
        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "my-key"
        });

        var result = await _controller.GetImageProxy(1, "/api/v3/system/status");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetImageProxy_returns_bad_request_when_path_contains_encoded_traversal()
    {
        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "my-key"
        });

        var result = await _controller.GetImageProxy(1, "/MediaCover/%2e%2e/api/v3/system/status");

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task GetImageProxy_returns_not_found_when_connection_not_found()
    {
        _connectionFactory.Get(99).Returns((ArrConnectionDefinition)null);

        var result = await _controller.GetImageProxy(99, "/MediaCover/1/poster.jpg");

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetImageProxy_returns_not_found_when_connection_disabled()
    {
        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "my-key",
            Enable = false
        });

        var result = await _controller.GetImageProxy(1, "/MediaCover/1/poster.jpg");

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task GetImageProxy_proxies_image_successfully()
    {
        var handler = new MockHttpMessageHandler();
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var responseMsg = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(imageBytes)
        };
        responseMsg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        handler.EnqueueResponse(responseMsg);

        var httpClient = new HttpClient(handler);
        var controller = new ArrConnectionController(_connectionFactory, _arrSyncService, _webhookRegistration, httpClient);

        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "my-key"
        });

        var result = await controller.GetImageProxy(1, "/MediaCover/1/poster.jpg");

        Assert.That(result, Is.InstanceOf<FileContentResult>());
        var fileResult = (FileContentResult)result;
        Assert.That(fileResult.ContentType, Is.EqualTo("image/jpeg"));
        Assert.That(fileResult.FileContents, Is.EqualTo(imageBytes));
    }

    [Test]
    public async Task GetImageProxy_returns_status_code_when_remote_call_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NotFound, "Not found");
        var httpClient = new HttpClient(handler);
        var controller = new ArrConnectionController(_connectionFactory, _arrSyncService, _webhookRegistration, httpClient);

        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "my-key"
        });

        var result = await controller.GetImageProxy(1, "/MediaCover/999/poster.jpg");

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusResult = (StatusCodeResult)result;
        Assert.That(statusResult.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task GetImageProxy_disposes_response_when_remote_call_fails()
    {
        var handler = new MockHttpMessageHandler();
        var responseMsg = new DisposableTrackingHttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not found")
        };
        handler.EnqueueResponse(responseMsg);
        var httpClient = new HttpClient(handler);
        var controller = new ArrConnectionController(_connectionFactory, _arrSyncService, _webhookRegistration, httpClient);

        _connectionFactory.Get(1).Returns(new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "my-key"
        });

        await controller.GetImageProxy(1, "/MediaCover/999/poster.jpg");

        Assert.That(responseMsg.WasDisposed, Is.True);
    }

    private sealed class DisposableTrackingHttpResponseMessage : HttpResponseMessage
    {
        public bool WasDisposed { get; private set; }

        public DisposableTrackingHttpResponseMessage(HttpStatusCode statusCode)
            : base(statusCode)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                WasDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
