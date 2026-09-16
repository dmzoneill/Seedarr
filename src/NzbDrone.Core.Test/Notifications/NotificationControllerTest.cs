using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using Seedarr.Api.V1.Notifications;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class NotificationControllerTest
{
    private INotificationRepository _notificationRepository;
    private IWebhookDispatcher _webhookDispatcher;
    private ICustomScriptService _customScriptService;
    private NotificationController _controller;

    [SetUp]
    public void SetUp()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _webhookDispatcher = Substitute.For<IWebhookDispatcher>();
        _customScriptService = Substitute.For<ICustomScriptService>();
        _controller = new NotificationController(_notificationRepository, _webhookDispatcher, _customScriptService);
    }

    [Test]
    public void Create_with_null_resource_returns_bad_request()
    {
        var result = _controller.Create(null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestResult>());
    }

    [Test]
    public void Create_with_no_active_triggers_returns_bad_request()
    {
        var resource = new NotificationResource
        {
            Name = "Discord Channel",
            Implementation = "Discord",
            OnGrab = false,
            OnDownloadComplete = false,
            OnMediaInspected = false,
            OnExtractComplete = false,
            OnSeedGoalReached = false,
            OnTorrentDeleted = false,
            OnHealthIssue = false,
            OnHealthRestored = false,
            OnManualInteractionRequired = false,
            OnApplicationUpdate = false,
            Categories = new List<string> { "Movies" },
        };

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("At least one notification trigger must be enabled"));
    }

    [Test]
    public void Create_with_active_trigger_persists_and_returns_ok_with_categories()
    {
        var resource = new NotificationResource
        {
            Name = "Webhook Channel",
            Implementation = "Webhook",
            OnGrab = true,
            Categories = new List<string> { "Movies", "TV" },
            Tags = new List<int> { 1, 2 },
        };

        _notificationRepository.Insert(Arg.Any<NotificationDefinition>())
            .Returns(callInfo =>
            {
                var def = callInfo.Arg<NotificationDefinition>();
                def.Id = 42;
                return def;
            });

        var result = _controller.Create(resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var created = (NotificationResource)okResult.Value;

        Assert.That(created.Id, Is.EqualTo(42));
        Assert.That(created.Name, Is.EqualTo("Webhook Channel"));
        Assert.That(created.Categories, Is.EquivalentTo(new[] { "Movies", "TV" }));
        Assert.That(created.Tags, Is.EquivalentTo(new[] { 1, 2 }));

        _notificationRepository.Received(1).Insert(Arg.Is<NotificationDefinition>(d =>
            d.Name == "Webhook Channel" &&
            d.Categories.Contains("Movies") &&
            d.Categories.Contains("TV") &&
            d.Tags.Contains(1) &&
            d.Tags.Contains(2)));
    }

    [Test]
    public void Update_with_null_resource_returns_bad_request()
    {
        var result = _controller.Update(1, null);

        Assert.That(result.Result, Is.InstanceOf<BadRequestResult>());
    }

    [Test]
    public void Update_with_no_active_triggers_returns_bad_request()
    {
        var resource = new NotificationResource
        {
            Id = 1,
            Name = "Discord Channel",
            Implementation = "Discord",
            OnGrab = false,
            OnDownloadComplete = false,
            OnMediaInspected = false,
            OnExtractComplete = false,
            OnSeedGoalReached = false,
            OnTorrentDeleted = false,
            OnHealthIssue = false,
            OnHealthRestored = false,
            OnManualInteractionRequired = false,
            OnApplicationUpdate = false,
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("At least one notification trigger must be enabled"));
    }

    [Test]
    public void Update_with_nonexistent_id_returns_not_found()
    {
        var resource = new NotificationResource
        {
            Id = 999,
            Name = "Telegram Channel",
            Implementation = "Telegram",
            OnDownloadComplete = true,
        };

        _notificationRepository.Get(999).Returns((NotificationDefinition)null);

        var result = _controller.Update(999, resource);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Update_with_active_trigger_updates_and_returns_ok_with_categories()
    {
        var existing = new NotificationDefinition
        {
            Id = 10,
            Name = "Old Name",
            Implementation = "Webhook",
            OnGrab = true,
            Categories = new List<string> { "OldCategory" },
        };

        var resource = new NotificationResource
        {
            Id = 10,
            Name = "New Name",
            Implementation = "Webhook",
            OnSeedGoalReached = true,
            Categories = new List<string> { "Anime", "Documentary" },
            Tags = new List<int> { 5 },
        };

        _notificationRepository.Get(10).Returns(existing);

        var result = _controller.Update(10, resource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var updated = (NotificationResource)okResult.Value;

        Assert.That(updated.Id, Is.EqualTo(10));
        Assert.That(updated.Name, Is.EqualTo("New Name"));
        Assert.That(updated.Categories, Is.EquivalentTo(new[] { "Anime", "Documentary" }));
        Assert.That(updated.Tags, Is.EquivalentTo(new[] { 5 }));

        _notificationRepository.Received(1).Update(Arg.Is<NotificationDefinition>(d =>
            d.Id == 10 &&
            d.Name == "New Name" &&
            d.Categories.Contains("Anime") &&
            d.Categories.Contains("Documentary") &&
            d.Tags.Contains(5)));
    }

    [Test]
    public void GetAll_maps_categories_and_tags()
    {
        var list = new List<NotificationDefinition>
        {
            new NotificationDefinition
            {
                Id = 1,
                Name = "N1",
                Categories = new List<string> { "Cat1" },
                Tags = new List<int> { 10 },
            },
            new NotificationDefinition
            {
                Id = 2,
                Name = "N2",
                Categories = null,
                Tags = null,
            },
        };

        _notificationRepository.All().Returns(list);

        var result = _controller.GetAll();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var items = (List<NotificationResource>)okResult.Value;

        Assert.That(items.Count, Is.EqualTo(2));
        Assert.That(items[0].Categories, Is.EquivalentTo(new[] { "Cat1" }));
        Assert.That(items[0].Tags, Is.EquivalentTo(new[] { 10 }));
        Assert.That(items[1].Categories, Is.Not.Null);
        Assert.That(items[1].Categories, Is.Empty);
        Assert.That(items[1].Tags, Is.Not.Null);
        Assert.That(items[1].Tags, Is.Empty);
    }

    [Test]
    public async Task TestDirect_should_return_bad_request_when_resource_is_null()
    {
        var result = await _controller.TestDirect(null);
        Assert.That(result.Result, Is.InstanceOf<BadRequestResult>());
    }

    [Test]
    public async Task TestDirect_CustomScript_should_fail_when_path_is_empty()
    {
        var resource = new NotificationResource
        {
            Implementation = "CustomScript",
            Settings = "{\"path\":\"\"}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("Script path is required"));
    }

    [Test]
    public async Task TestDirect_CustomScript_should_fail_when_path_contains_null_byte()
    {
        var resource = new NotificationResource
        {
            Implementation = "CustomScript",
            Settings = "{\"path\":\"/usr/bin/script\0.sh\"}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("invalid characters"));
    }

    [Test]
    public async Task TestDirect_CustomScript_should_fail_when_path_is_relative()
    {
        var resource = new NotificationResource
        {
            Implementation = "CustomScript",
            Settings = "{\"path\":\"relative/path/script.sh\"}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("absolute path"));
    }

    [TestCase("/tmp/exploit.sh")]
    [TestCase("/var/tmp/script.sh")]
    [TestCase("/home/user/my_script.sh")]
    [TestCase("/root/run.sh")]
    public async Task TestDirect_CustomScript_should_fail_when_path_is_outside_authorized_directories(string unauthorizedPath)
    {
        var resource = new NotificationResource
        {
            Implementation = "CustomScript",
            Settings = $"{{\"path\":\"{unauthorizedPath}\"}}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("not permitted"));
    }

    [Test]
    public async Task TestDirect_CustomScript_should_fail_when_file_does_not_exist_in_authorized_directory()
    {
        var nonexistentPath = OperatingSystem.IsWindows()
            ? @"C:\Program Files\Seedarr\Scripts\nonexistent_test_script_9999.bat"
            : "/usr/local/bin/nonexistent_test_script_9999.sh";

        var resource = new NotificationResource
        {
            Implementation = "CustomScript",
            Settings = $"{{\"path\":\"{nonexistentPath}\"}}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("does not exist"));
    }

    [TestCase("http://127.0.0.1:8080/webhook")]
    [TestCase("http://localhost:5000/api")]
    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("http://metadata.google.internal/computeMetadata/v1/")]
    public async Task Test_Webhook_should_reject_ssrf_urls(string prohibitedUrl)
    {
        var resource = new NotificationResource
        {
            Implementation = "Webhook",
            Settings = $"{{\"url\":\"{prohibitedUrl}\"}}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("SSRF protection"));
    }

    [Test]
    public async Task Test_Webhook_should_fail_when_url_is_missing()
    {
        var resource = new NotificationResource
        {
            Implementation = "Webhook",
            Settings = "{\"url\":\"\"}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("URL is required"));
    }

    [Test]
    public async Task Test_Webhook_should_return_detailed_diagnostic_on_http_failure()
    {
        var publicUrl = "https://93.184.216.34/webhook";
        _webhookDispatcher.DispatchDetailedAsync(Arg.Is(publicUrl), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new WebhookDispatchResult
            {
                Success = false,
                StatusCode = HttpStatusCode.InternalServerError,
                Message = "Webhook endpoint returned HTTP 500 (Internal Server Error).",
            });

        var resource = new NotificationResource
        {
            Implementation = "Webhook",
            Settings = $"{{\"url\":\"{publicUrl}\"}}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("HTTP 500"));
    }

    [TestCase("127.0.0.1")]
    [TestCase("localhost")]
    [TestCase("169.254.169.254")]
    public async Task Test_Email_should_fail_when_host_is_unsafe(string unsafeHost)
    {
        var resource = new NotificationResource
        {
            Implementation = "Email",
            Settings = $"{{\"host\":\"{unsafeHost}\",\"port\":587,\"to\":\"user@example.com\",\"from\":\"test@example.com\"}}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("not permitted"));
    }

    [TestCase(22)]
    [TestCase(80)]
    [TestCase(8080)]
    public async Task Test_Email_should_fail_when_port_is_prohibited(int prohibitedPort)
    {
        var resource = new NotificationResource
        {
            Implementation = "Email",
            Settings = $"{{\"host\":\"93.184.216.34\",\"port\":{prohibitedPort},\"to\":\"user@example.com\",\"from\":\"test@example.com\"}}",
        };

        var actionResult = await _controller.TestDirect(resource);
        var okResult = actionResult.Result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);

        var testResult = okResult.Value as NotificationTestResult;
        Assert.That(testResult, Is.Not.Null);
        Assert.That(testResult.Success, Is.False);
        Assert.That(testResult.Message, Does.Contain("port"));
        Assert.That(testResult.Message, Does.Contain("is not permitted"));
    }
}
