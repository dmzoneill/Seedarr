using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Notifications;
using Seedarr.Http;

namespace Seedarr.Api.V1.Notifications;

/// <summary>
/// Manages outbound notification endpoints, alerting integrations, and webhook triggers.
/// </summary>
[V1ApiController("notifications")]
[Route("api/v1/notification")]
public class NotificationController : Controller
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IWebhookDispatcher _webhookDispatcher;
    private readonly ICustomScriptService _customScriptService;

    public NotificationController(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService)
    {
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
        _customScriptService = customScriptService;
    }

    /// <summary>
    /// Retrieves all configured notification destinations.
    /// </summary>
    [HttpGet]
    public ActionResult<List<NotificationResource>> GetAll()
    {
        return Ok(_notificationRepository.All().Select(ToResource).ToList());
    }

    /// <summary>
    /// Retrieves a specific notification configuration by its unique identifier.
    /// </summary>
    [HttpGet("{id:int}")]
    public ActionResult<NotificationResource> GetById(int id)
    {
        var item = _notificationRepository.Get(id);
        if (item == null)
        {
            return NotFound();
        }

        return Ok(ToResource(item));
    }

    /// <summary>
    /// Creates a new notification definition.
    /// </summary>
    [HttpPost]
    public ActionResult<NotificationResource> Create([FromBody] NotificationResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        var created = _notificationRepository.Insert(model);
        return Ok(ToResource(created));
    }

    /// <summary>
    /// Updates an existing notification configuration.
    /// </summary>
    [HttpPut("{id:int}")]
    public ActionResult<NotificationResource> Update(int id, [FromBody] NotificationResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var existing = _notificationRepository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        _notificationRepository.Update(model);
        return Ok(ToResource(model));
    }

    /// <summary>
    /// Updates an existing notification configuration (body-based ID).
    /// </summary>
    [HttpPut]
    public ActionResult<NotificationResource> UpdateWithoutId([FromBody] NotificationResource resource)
    {
        if (resource == null || resource.Id <= 0)
        {
            return BadRequest();
        }

        return Update(resource.Id, resource);
    }

    /// <summary>
    /// Deletes a notification destination by its ID.
    /// </summary>
    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        _notificationRepository.Delete(id);
        return Ok();
    }

    /// <summary>
    /// Tests a saved notification configuration by sending a test payload.
    /// </summary>
    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<NotificationTestResult>> Test(int id)
    {
        var item = _notificationRepository.Get(id);
        if (item == null)
        {
            return NotFound();
        }

        return await TestInternal(item);
    }

    /// <summary>
    /// Tests a transient notification configuration without saving it first.
    /// </summary>
    [HttpPost("test")]
    public async Task<ActionResult<NotificationTestResult>> TestDirect([FromBody] NotificationResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        return await TestInternal(model);
    }

    private async Task<ActionResult<NotificationTestResult>> TestInternal(NotificationDefinition notif)
    {
        object payload;
        if (string.Equals(notif.Implementation, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            payload = new
            {
                username = "Seedarr",
                embeds = new object[]
                {
                    new
                    {
                        title = "[Test] Seedarr Notification Test",
                        description = "This is a test notification from Seedarr. Your webhook configuration is working properly.",
                        color = 16765286,
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                },
            };
        }
        else if (string.Equals(notif.Implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var chatId = ExtractSetting(notif.Settings, "chat_id", "chatId");
            var telegramPayload = new Dictionary<string, object>
            {
                ["text"] = "*Seedarr Test Notification*\nYour Telegram notification connection is working properly.",
                ["parse_mode"] = "Markdown",
            };

            if (!string.IsNullOrEmpty(chatId))
            {
                telegramPayload["chat_id"] = chatId;
            }

            payload = telegramPayload;
        }
        else if (string.Equals(notif.Implementation, "Gotify", StringComparison.OrdinalIgnoreCase))
        {
            payload = new
            {
                title = "Seedarr: Test",
                message = "This is a test notification from Seedarr.",
                priority = 5,
            };
        }
        else if (string.Equals(notif.Implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            var token = ExtractSetting(notif.Settings, "token", "botToken", "apiKey");
            var user = ExtractSetting(notif.Settings, "user", "userKey");
            var pushoverPayload = new Dictionary<string, object>
            {
                ["title"] = "Seedarr: Test",
                ["message"] = "This is a test notification from Seedarr.",
            };

            if (!string.IsNullOrEmpty(token))
            {
                pushoverPayload["token"] = token;
            }

            if (!string.IsNullOrEmpty(user))
            {
                pushoverPayload["user"] = user;
            }

            payload = pushoverPayload;
        }
        else if (string.Equals(notif.Implementation, "Apprise", StringComparison.OrdinalIgnoreCase))
        {
            payload = new
            {
                title = "Seedarr: Test",
                body = "This is a test notification from Seedarr. Your Apprise integration is working properly.",
                type = "info",
            };
        }
        else if (string.Equals(notif.Implementation, "Slack", StringComparison.OrdinalIgnoreCase))
        {
            payload = new
            {
                text = "*Seedarr Test Notification*\nYour Slack notification webhook is working properly.",
                username = "Seedarr",
            };
        }
        else
        {
            payload = new
            {
                EventType = "Test",
                Message = "Seedarr test notification",
                Timestamp = DateTime.UtcNow,
            };
        }

        if (string.Equals(notif.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
        {
            var (scriptPath, scriptArgs) = CustomScriptService.ParseSettings(notif.Settings);
            var success = await _customScriptService.ExecuteScriptAsync(scriptPath, null, "Test", scriptArgs);
            return Ok(new NotificationTestResult
            {
                Success = success,
                Message = success ? "Script executed successfully." : "Script execution failed.",
            });
        }
        else if (string.Equals(notif.Implementation, "Email", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                NotificationEventHandler.SendEmailNotification(notif.Settings, "Test", null, null, payload);
                return Ok(new NotificationTestResult
                {
                    Success = true,
                    Message = "Email test notification sent successfully.",
                });
            }
            catch (Exception ex)
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = $"Failed to send email test notification: {ex.Message}",
                });
            }
        }
        else
        {
            var targetUrl = NotificationEventHandler.ResolveTargetUrl(notif.Implementation, notif.Settings);
            var customHeaders = NotificationEventHandler.ResolveCustomHeaders(notif.Implementation, notif.Settings);
            var success = await _webhookDispatcher.DispatchAsync(targetUrl, payload, customHeaders);
            return Ok(new NotificationTestResult
            {
                Success = success,
                Message = success ? "Webhook dispatched successfully." : "Webhook dispatch failed.",
            });
        }
    }

    private static NotificationResource ToResource(NotificationDefinition n)
    {
        return new NotificationResource
        {
            Id = n.Id,
            Name = n.Name,
            Implementation = n.Implementation,
            ConfigContract = n.ConfigContract,
            Settings = n.Settings,
            Enable = n.Enable,
            OnGrab = n.OnGrab,
            OnDownloadComplete = n.OnDownloadComplete,
            OnMediaInspected = n.OnMediaInspected,
            OnExtractComplete = n.OnExtractComplete,
            OnSeedGoalReached = n.OnSeedGoalReached,
            OnTorrentDeleted = n.OnTorrentDeleted,
            OnHealthIssue = n.OnHealthIssue,
            OnHealthRestored = n.OnHealthRestored,
            OnManualInteractionRequired = n.OnManualInteractionRequired,
            OnApplicationUpdate = n.OnApplicationUpdate,
            Tags = n.Tags ?? new List<int>(),
        };
    }

    private static NotificationDefinition ToModel(NotificationResource r)
    {
        return new NotificationDefinition
        {
            Id = r.Id,
            Name = r.Name,
            Implementation = r.Implementation ?? "Webhook",
            ConfigContract = r.ConfigContract,
            Settings = r.Settings,
            Enable = r.Enable,
            OnGrab = r.OnGrab,
            OnDownloadComplete = r.OnDownloadComplete,
            OnMediaInspected = r.OnMediaInspected,
            OnExtractComplete = r.OnExtractComplete,
            OnSeedGoalReached = r.OnSeedGoalReached,
            OnTorrentDeleted = r.OnTorrentDeleted,
            OnHealthIssue = r.OnHealthIssue,
            OnHealthRestored = r.OnHealthRestored,
            OnManualInteractionRequired = r.OnManualInteractionRequired,
            OnApplicationUpdate = r.OnApplicationUpdate,
            Tags = r.Tags ?? new List<int>(),
        };
    }

    private static string ExtractSetting(string settings, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return string.Empty;
        }

        if (settings.TrimStart().StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(settings);
                var root = doc.RootElement;
                foreach (var prop in propertyNames)
                {
                    if (root.TryGetProperty(prop, out var val))
                    {
                        return val.GetString() ?? val.ToString();
                    }
                }
            }
            catch
            {
            }
        }

        foreach (var prop in propertyNames)
        {
            if (settings.Contains(prop + "="))
            {
                var escapedProp = Regex.Escape(prop);
                var match = Regex.Match(settings, $@"{escapedProp}=([^&]+)");
                if (match.Success)
                {
                    return Uri.UnescapeDataString(match.Groups[1].Value);
                }
            }
        }

        return string.Empty;
    }
}
