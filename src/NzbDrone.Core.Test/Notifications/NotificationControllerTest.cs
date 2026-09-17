using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
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
    private INotificationRepository _repository;
    private IWebhookDispatcher _dispatcher;
    private ICustomScriptService _scriptService;
    private INotificationFactory _factory;
    private NotificationController _controller;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<INotificationRepository>();
        _dispatcher = Substitute.For<IWebhookDispatcher>();
        _scriptService = Substitute.For<ICustomScriptService>();
        _factory = Substitute.For<INotificationFactory>();

        _controller = new NotificationController(
            _repository,
            _dispatcher,
            _scriptService,
            _factory);
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
            OnBackupComplete = false,
            OnBackupFailed = false,
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

        _repository.Insert(Arg.Any<NotificationDefinition>())
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

        _repository.Received(1).Insert(Arg.Is<NotificationDefinition>(d =>
            d.Name == "Webhook Channel" &&
            d.Categories.Contains("Movies") &&
            d.Categories.Contains("TV") &&
            d.Tags.Contains(1) &&
            d.Tags.Contains(2)));
    }

    [Test]
    public void ToResource_masks_password_in_email_settings()
    {
        var definition = new NotificationDefinition
        {
            Id = 1,
            Name = "My Email",
            Implementation = "Email",
            Settings = "{\"server\":\"smtp.mail.com\",\"port\":587,\"username\":\"user@mail.com\",\"password\":\"SuperSecret123\",\"recipient\":\"to@mail.com\"}"
        };

        var resource = NotificationController.ToResource(definition);

        Assert.That(resource, Is.Not.Null);
        using var doc = JsonDocument.Parse(resource.Settings);
        Assert.That(doc.RootElement.GetProperty("password").GetString(), Is.EqualTo(NotificationController.PasswordMask));
        Assert.That(doc.RootElement.GetProperty("username").GetString(), Is.EqualTo("user@mail.com"));
        Assert.That(doc.RootElement.GetProperty("server").GetString(), Is.EqualTo("smtp.mail.com"));
    }

    [Test]
    public void ToResource_masks_token_in_telegram_settings()
    {
        var definition = new NotificationDefinition
        {
            Id = 2,
            Name = "My Telegram",
            Implementation = "Telegram",
            Settings = "{\"token\":\"123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11\",\"chat_id\":\"987654\"}"
        };

        var resource = NotificationController.ToResource(definition);

        Assert.That(resource, Is.Not.Null);
        using var doc = JsonDocument.Parse(resource.Settings);
        Assert.That(doc.RootElement.GetProperty("token").GetString(), Is.EqualTo(NotificationController.PasswordMask));
        Assert.That(doc.RootElement.GetProperty("chat_id").GetString(), Is.EqualTo("987654"));
    }

    [Test]
    public void ToResource_masks_token_and_user_and_userKey_in_pushover_settings()
    {
        var definition = new NotificationDefinition
        {
            Id = 3,
            Name = "My Pushover",
            Implementation = "Pushover",
            Settings = "{\"token\":\"app_token_secret\",\"user\":\"user_key_secret\",\"userKey\":\"another_key\"}"
        };

        var resource = NotificationController.ToResource(definition);

        Assert.That(resource, Is.Not.Null);
        using var doc = JsonDocument.Parse(resource.Settings);
        Assert.That(doc.RootElement.GetProperty("token").GetString(), Is.EqualTo(NotificationController.PasswordMask));
        Assert.That(doc.RootElement.GetProperty("user").GetString(), Is.EqualTo(NotificationController.PasswordMask));
        Assert.That(doc.RootElement.GetProperty("userKey").GetString(), Is.EqualTo(NotificationController.PasswordMask));
    }

    [Test]
    public void ToResource_masks_discord_webhook_secret_token_in_url()
    {
        var definition = new NotificationDefinition
        {
            Id = 4,
            Name = "My Discord",
            Implementation = "Discord",
            Settings = "{\"url\":\"https://discord.com/api/webhooks/1234567890/AbCdEfGhIjKlMnOpQrStUvWxYz\",\"username\":\"Seedarr\"}"
        };

        var resource = NotificationController.ToResource(definition);

        Assert.That(resource, Is.Not.Null);
        using var doc = JsonDocument.Parse(resource.Settings);
        var maskedUrl = doc.RootElement.GetProperty("url").GetString();
        Assert.That(maskedUrl, Is.EqualTo("https://discord.com/api/webhooks/1234567890/********"));
        Assert.That(doc.RootElement.GetProperty("username").GetString(), Is.EqualTo("Seedarr"));
    }

    [Test]
    public void ToResource_masks_slack_webhook_secret_token_in_url()
    {
        var definition = new NotificationDefinition
        {
            Id = 5,
            Name = "My Slack",
            Implementation = "Slack",
            Settings = "{\"url\":\"https://hooks.slack.com/services/T00000000/B00000000/SECRETTOKEN12345\"}"
        };

        var resource = NotificationController.ToResource(definition);

        Assert.That(resource, Is.Not.Null);
        using var doc = JsonDocument.Parse(resource.Settings);
        var maskedUrl = doc.RootElement.GetProperty("url").GetString();
        Assert.That(maskedUrl, Is.EqualTo("https://hooks.slack.com/services/T00000000/B00000000/********"));
    }

    [Test]
    public void ToResource_masks_gotify_query_token_in_url()
    {
        var definition = new NotificationDefinition
        {
            Id = 6,
            Name = "My Gotify",
            Implementation = "Gotify",
            Settings = "{\"url\":\"https://gotify.example.com/message?token=my_secret_token\",\"token\":\"my_secret_token\"}"
        };

        var resource = NotificationController.ToResource(definition);

        Assert.That(resource, Is.Not.Null);
        using var doc = JsonDocument.Parse(resource.Settings);
        Assert.That(doc.RootElement.GetProperty("url").GetString(), Is.EqualTo("https://gotify.example.com/message?token=********"));
        Assert.That(doc.RootElement.GetProperty("token").GetString(), Is.EqualTo(NotificationController.PasswordMask));
    }

    [Test]
    public void ToResource_masks_basic_auth_in_url()
    {
        var definition = new NotificationDefinition
        {
            Id = 7,
            Name = "My Webhook",
            Implementation = "Webhook",
            Settings = "{\"url\":\"https://admin:mysecretpassword@webhook.example.com/api\"}"
        };

        var resource = NotificationController.ToResource(definition);

        Assert.That(resource, Is.Not.Null);
        using var doc = JsonDocument.Parse(resource.Settings);
        Assert.That(doc.RootElement.GetProperty("url").GetString(), Is.EqualTo("https://admin:********@webhook.example.com/api"));
    }

    [Test]
    public void ToResource_returns_null_when_definition_is_null()
    {
        Assert.That(NotificationController.ToResource(null), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ToResource_handles_null_or_empty_settings(string emptySettings)
    {
        var definition = new NotificationDefinition
        {
            Id = 8,
            Name = "Empty",
            Implementation = "Webhook",
            Settings = emptySettings
        };

        var resource = NotificationController.ToResource(definition);

        Assert.That(resource, Is.Not.Null);
        Assert.That(resource.Settings, Is.EqualTo(emptySettings));
    }

    [Test]
    public void GetAll_maps_categories_and_tags()
    {
        var list = new List<NotificationDefinition>
        {
            new()
            {
                Id = 1,
                Name = "N1",
                Categories = new List<string> { "Cat1" },
                Tags = new List<int> { 10 }
            },
            new()
            {
                Id = 2,
                Name = "N2",
                Categories = null,
                Tags = null
            }
        };

        _repository.All().Returns(list);

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
    public void GetAll_returns_masked_resources()
    {
        var list = new List<NotificationDefinition>
        {
            new() { Id = 1, Name = "Tele", Implementation = "Telegram", Settings = "{\"token\":\"token123\"}" },
            new() { Id = 2, Name = "Mail", Implementation = "Email", Settings = "{\"password\":\"pass123\"}" }
        };
        _repository.All().Returns(list);

        var result = _controller.GetAll();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var resources = (List<NotificationResource>)okResult.Value;
        Assert.That(resources, Has.Count.EqualTo(2));
        Assert.That(resources[0].Settings, Does.Contain(NotificationController.PasswordMask));
        Assert.That(resources[1].Settings, Does.Contain(NotificationController.PasswordMask));
    }

    [Test]
    public void GetById_returns_masked_resource()
    {
        var definition = new NotificationDefinition
        {
            Id = 10,
            Name = "Tele",
            Implementation = "Telegram",
            Settings = "{\"token\":\"secrettoken\"}"
        };
        _repository.Get(10).Returns(definition);

        var result = _controller.GetById(10);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var resource = (NotificationResource)okResult.Value;
        Assert.That(resource.Settings, Does.Contain(NotificationController.PasswordMask));
    }

    [Test]
    public void GetById_returns_not_found_when_missing()
    {
        _repository.Get(99).Returns((NotificationDefinition)null);

        var result = _controller.GetById(99);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public void Create_saves_definition_and_returns_masked_resource()
    {
        var inputResource = new NotificationResource
        {
            Name = "New Email",
            Implementation = "Email",
            OnGrab = true,
            Settings = "{\"username\":\"user@mail.com\",\"password\":\"plaintext_password\"}"
        };

        _repository.Insert(Arg.Any<NotificationDefinition>()).Returns(callInfo =>
        {
            var def = callInfo.Arg<NotificationDefinition>();
            def.Id = 15;
            return def;
        });

        var result = _controller.Create(inputResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var returned = (NotificationResource)okResult.Value;

        // Saved definition in DB had plaintext password
        _repository.Received(1).Insert(Arg.Is<NotificationDefinition>(d =>
            d.Settings.Contains("plaintext_password")));

        // Returned resource to client has masked password
        Assert.That(returned.Settings, Does.Contain(NotificationController.PasswordMask));
        Assert.That(returned.Settings, Does.Not.Contain("plaintext_password"));
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
            OnBackupComplete = false,
            OnBackupFailed = false,
        };

        var result = _controller.Update(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        Assert.That(badRequest.Value, Is.EqualTo("At least one notification trigger must be enabled"));
    }

    [Test]
    public void Update_preserves_existing_password_when_masked_asterisks_submitted()
    {
        var existing = new NotificationDefinition
        {
            Id = 1,
            Name = "Mail",
            Implementation = "Email",
            OnGrab = true,
            Settings = "{\"server\":\"smtp.mail.com\",\"username\":\"user\",\"password\":\"StoredSecretPassword\"}"
        };
        _repository.Get(1).Returns(existing);

        var updateResource = new NotificationResource
        {
            Id = 1,
            Name = "Mail Renamed",
            Implementation = "Email",
            OnGrab = true,
            Settings = "{\"server\":\"smtp.mail.com\",\"username\":\"user\",\"password\":\"********\"}"
        };

        var result = _controller.Update(1, updateResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _repository.Received(1).Update(Arg.Is<NotificationDefinition>(d =>
            d.Settings.Contains("StoredSecretPassword") && !d.Settings.Contains("********")));
    }

    [Test]
    public void Update_preserves_existing_password_when_empty_string_submitted()
    {
        var existing = new NotificationDefinition
        {
            Id = 1,
            Name = "Mail",
            Implementation = "Email",
            OnGrab = true,
            Settings = "{\"server\":\"smtp.mail.com\",\"username\":\"user\",\"password\":\"StoredSecretPassword\"}"
        };
        _repository.Get(1).Returns(existing);

        var updateResource = new NotificationResource
        {
            Id = 1,
            Name = "Mail Renamed",
            Implementation = "Email",
            OnGrab = true,
            Settings = "{\"server\":\"smtp.mail.com\",\"username\":\"user\",\"password\":\"\"}"
        };

        var result = _controller.Update(1, updateResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _repository.Received(1).Update(Arg.Is<NotificationDefinition>(d =>
            d.Settings.Contains("StoredSecretPassword")));
    }

    [Test]
    public void Update_updates_password_when_new_unmasked_value_submitted()
    {
        var existing = new NotificationDefinition
        {
            Id = 1,
            Name = "Mail",
            Implementation = "Email",
            OnGrab = true,
            Settings = "{\"server\":\"smtp.mail.com\",\"username\":\"user\",\"password\":\"OldPassword\"}"
        };
        _repository.Get(1).Returns(existing);

        var updateResource = new NotificationResource
        {
            Id = 1,
            Name = "Mail",
            Implementation = "Email",
            OnGrab = true,
            Settings = "{\"server\":\"smtp.mail.com\",\"username\":\"user\",\"password\":\"BrandNewPassword\"}"
        };

        var result = _controller.Update(1, updateResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _repository.Received(1).Update(Arg.Is<NotificationDefinition>(d =>
            d.Settings.Contains("BrandNewPassword") && !d.Settings.Contains("OldPassword")));
    }

    [Test]
    public void Update_preserves_existing_telegram_token_when_masked()
    {
        var existing = new NotificationDefinition
        {
            Id = 2,
            Name = "Tele",
            Implementation = "Telegram",
            OnGrab = true,
            Settings = "{\"token\":\"123456:ABC-DEF1234ghIkl\",\"chat_id\":\"123\"}"
        };
        _repository.Get(2).Returns(existing);

        var updateResource = new NotificationResource
        {
            Id = 2,
            Name = "Tele",
            Implementation = "Telegram",
            OnGrab = true,
            Settings = "{\"token\":\"********\",\"chat_id\":\"456\"}"
        };

        var result = _controller.Update(2, updateResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _repository.Received(1).Update(Arg.Is<NotificationDefinition>(d =>
            d.Settings.Contains("123456:ABC-DEF1234ghIkl") && d.Settings.Contains("456")));
    }

    [Test]
    public void Update_preserves_existing_discord_webhook_url_when_masked()
    {
        var existing = new NotificationDefinition
        {
            Id = 3,
            Name = "Discord",
            Implementation = "Discord",
            OnGrab = true,
            Settings = "{\"url\":\"https://discord.com/api/webhooks/12345/REAL_DISCORD_TOKEN\",\"username\":\"Seedarr\"}"
        };
        _repository.Get(3).Returns(existing);

        var updateResource = new NotificationResource
        {
            Id = 3,
            Name = "Discord",
            Implementation = "Discord",
            OnGrab = true,
            Settings = "{\"url\":\"https://discord.com/api/webhooks/12345/********\",\"username\":\"UpdatedSeedarr\"}"
        };

        var result = _controller.Update(3, updateResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _repository.Received(1).Update(Arg.Is<NotificationDefinition>(d =>
            d.Settings.Contains("REAL_DISCORD_TOKEN") && d.Settings.Contains("UpdatedSeedarr")));
    }

    [Test]
    public void Update_preserves_existing_pushover_credentials_when_masked()
    {
        var existing = new NotificationDefinition
        {
            Id = 4,
            Name = "Pushover",
            Implementation = "Pushover",
            OnGrab = true,
            Settings = "{\"token\":\"stored_token\",\"user\":\"stored_user\"}"
        };
        _repository.Get(4).Returns(existing);

        var updateResource = new NotificationResource
        {
            Id = 4,
            Name = "Pushover",
            Implementation = "Pushover",
            OnGrab = true,
            Settings = "{\"token\":\"********\",\"user\":\"********\"}"
        };

        var result = _controller.Update(4, updateResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        _repository.Received(1).Update(Arg.Is<NotificationDefinition>(d =>
            d.Settings.Contains("stored_token") && d.Settings.Contains("stored_user")));
    }

    [Test]
    public void Update_returns_not_found_when_missing()
    {
        _repository.Get(99).Returns((NotificationDefinition)null);

        var result = _controller.Update(99, new NotificationResource { Id = 99, OnGrab = true });

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task TestDirect_with_existing_id_restores_stored_credentials_before_test()
    {
        var existing = new NotificationDefinition
        {
            Id = 5,
            Name = "Webhook",
            Implementation = "Webhook",
            Settings = "{\"url\":\"https://webhook.example.com/api?token=REAL_SECRET_TOKEN\"}"
        };
        _repository.Get(5).Returns(existing);

        _dispatcher.DispatchDetailedAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
            .Returns(new WebhookDispatchResult { Success = true, Message = "OK" });

        var testResource = new NotificationResource
        {
            Id = 5,
            Implementation = "Webhook",
            OnSeedGoalReached = true,
            Categories = new List<string> { "Anime", "Documentary" },
            Tags = new List<int> { 5 },
            Settings = "{\"url\":\"https://webhook.example.com/api?token=********\"}"
        };

        var result = await _controller.TestDirect(testResource);

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        await _dispatcher.Received(1).DispatchDetailedAsync(
            Arg.Is<string>(url => url.Contains("REAL_SECRET_TOKEN")),
            Arg.Any<object>(),
            Arg.Any<string>());
    }

    [Test]
    public void GetSchema_returns_available_schemas_from_factory()
    {
        var definitions = new List<NotificationDefinition>
        {
            new() { Name = "Discord", Implementation = "Discord", ConfigContract = "DiscordSettings" },
            new() { Name = "Telegram", Implementation = "Telegram", ConfigContract = "TelegramSettings" },
            new() { Name = "Email", Implementation = "Email", ConfigContract = "EmailSettings" }
        };
        _factory.GetDefaultDefinitions().Returns(definitions);

        var result = _controller.GetSchema();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var schemas = (List<NotificationResource>)okResult.Value;

        Assert.That(schemas, Has.Count.EqualTo(3));
        Assert.That(schemas.Select(s => s.Implementation), Does.Contain("Discord"));
        Assert.That(schemas.Select(s => s.Implementation), Does.Contain("Telegram"));
        Assert.That(schemas.Select(s => s.Implementation), Does.Contain("Email"));
    }

    [Test]
    public void GetSchema_returns_empty_when_factory_is_null()
    {
        var controller = new NotificationController(_repository, _dispatcher, _scriptService, null);

        var result = controller.GetSchema();

        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result.Result;
        var schemas = (List<NotificationResource>)okResult.Value;

        Assert.That(schemas, Is.Empty);
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
        _dispatcher.DispatchDetailedAsync(Arg.Is(publicUrl), Arg.Any<object>(), Arg.Any<string>())
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
