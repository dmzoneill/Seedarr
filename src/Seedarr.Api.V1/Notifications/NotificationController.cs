using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Serializer;
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
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    public const string PasswordMask = "********"; // NOSONAR

    private readonly INotificationRepository _notificationRepository;
    private readonly IWebhookDispatcher _webhookDispatcher;
    private readonly ICustomScriptService _customScriptService;
    private readonly INotificationFactory _notificationFactory;

    public NotificationController(
        INotificationRepository notificationRepository,
        IWebhookDispatcher webhookDispatcher,
        ICustomScriptService customScriptService,
        INotificationFactory notificationFactory = null)
    {
        _notificationRepository = notificationRepository;
        _webhookDispatcher = webhookDispatcher;
        _customScriptService = customScriptService;
        _notificationFactory = notificationFactory;
    }

    /// <summary>
    /// Retrieves default schemas for all supported notification providers.
    /// </summary>
    [HttpGet("schema")]
    public ActionResult<List<NotificationResource>> GetSchema()
    {
        if (_notificationFactory == null)
        {
            return Ok(new List<NotificationResource>());
        }

        var schemas = _notificationFactory.GetDefaultDefinitions()
            .Select(ToResource)
            .ToList();

        return Ok(schemas);
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

        if (!HasActiveTrigger(resource))
        {
            return BadRequest("At least one notification trigger must be enabled");
        }

        if (SettingsContainMaskedSecrets(resource.Settings, resource.Implementation))
        {
            if (resource.Id <= 0)
            {
                return BadRequest("Settings contain masked secrets; provide full credentials when creating a new notification.");
            }

            var source = _notificationRepository.Get(resource.Id);
            if (source == null)
            {
                return BadRequest("Settings contain masked secrets; provide full credentials or a valid existing notification id to copy credentials from.");
            }

            resource.Settings = RestoreSecrets(resource.Settings, source.Settings, resource.Implementation ?? source.Implementation);
        }

        resource.Id = 0;

        var fallbackError = ValidateFallbackNotificationId(resource);
        if (fallbackError != null)
        {
            return BadRequest(fallbackError);
        }

        var model = ToModel(resource);
        var created = _notificationRepository.Insert(model);
        return Ok(ToResource(created));
    }

    /// <summary>
    /// Updates an existing notification configuration.
    /// </summary>
    [HttpPut("{id:int}")]
    public ActionResult<NotificationResource> Update(int id, [FromBody] JsonElement body)
    {
        if (body.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return BadRequest();
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return BadRequest();
        }

        var presentPropertyKeys = body.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resource = JsonSerializer.Deserialize<NotificationResource>(body, STJson.GetSerializerSettings());
        if (resource == null)
        {
            return BadRequest();
        }

        return UpdateNotification(id, resource, presentPropertyKeys);
    }

    [NonAction]
    public ActionResult<NotificationResource> Update(int id, NotificationResource resource) =>
        UpdateNotification(id, resource, null);

    private ActionResult<NotificationResource> UpdateNotification(
        int id,
        NotificationResource resource,
        IReadOnlySet<string> presentPropertyKeys)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        if (presentPropertyKeys == null && !HasActiveTrigger(resource))
        {
            return BadRequest("At least one notification trigger must be enabled");
        }

        var existing = _notificationRepository.Get(id);
        if (existing == null)
        {
            return NotFound();
        }

        var model = presentPropertyKeys == null
            ? ToModel(resource)
            : NotificationUpdateMerger.Merge(existing, resource, presentPropertyKeys);
        model.Id = id;

        if (presentPropertyKeys != null && !HasActiveTrigger(model))
        {
            return BadRequest("At least one notification trigger must be enabled");
        }

        var fallbackError = ValidateFallbackNotificationId(ToResource(model), id);
        if (fallbackError != null)
        {
            return BadRequest(fallbackError);
        }

        model.Settings = RestoreSecrets(
            model.Settings,
            existing.Settings,
            model.Implementation ?? existing.Implementation);

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

    private static readonly string[] AllowedScriptDirectories = OperatingSystem.IsWindows()
        ? new[] { @"C:\Program Files\Seedarr\Scripts", @"C:\ProgramData\Seedarr\Scripts" }
        : new[] { "/usr/local/bin", "/usr/bin", "/opt/seedarr/scripts", "/var/lib/seedarr/scripts", "/etc/seedarr/scripts" };

    private static bool IsInAllowedDirectory(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        foreach (var dir in AllowedScriptDirectories)
        {
            var fullDir = Path.GetFullPath(dir);
            if (!fullDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                fullDir += Path.DirectorySeparatorChar;
            }

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (fullPath.StartsWith(fullDir, comparison) || string.Equals(fullPath, Path.GetFullPath(dir), comparison))
            {
                return true;
            }
        }

        return false;
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

        if (resource.Id > 0)
        {
            var existing = _notificationRepository.Get(resource.Id);
            if (existing != null)
            {
                resource.Settings = RestoreSecrets(resource.Settings, existing.Settings, resource.Implementation ?? existing.Implementation);
            }
        }

        if (string.Equals(resource.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
        {
            var (scriptPath, _) = CustomScriptService.ParseSettings(resource.Settings);
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = "Script path is required.",
                });
            }

            if (scriptPath.Contains('\0') || scriptPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = "Script path contains invalid characters.",
                });
            }

            if (!Path.IsPathRooted(scriptPath))
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = "Script path must be an absolute path.",
                });
            }

            var fullPath = Path.GetFullPath(scriptPath);

            var isSavedScript = false;
            if (resource.Id > 0)
            {
                var existing = _notificationRepository.Get(resource.Id);
                if (existing != null && string.Equals(existing.Implementation, "CustomScript", StringComparison.OrdinalIgnoreCase))
                {
                    var (savedPath, _) = CustomScriptService.ParseSettings(existing.Settings);
                    if (string.Equals(savedPath, scriptPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        isSavedScript = true;
                    }
                }
            }

            if (!isSavedScript && !IsInAllowedDirectory(fullPath))
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = $"Custom script path '{fullPath}' is not permitted. Transient scripts must be located within authorized directories ({string.Join(", ", AllowedScriptDirectories)}).",
                });
            }

            if (!global::System.IO.File.Exists(fullPath))
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = $"Script file does not exist: {fullPath}",
                });
            }
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
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = "Script path is required.",
                });
            }

            if (scriptPath.Contains('\0') || scriptPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = "Script path contains invalid characters.",
                });
            }

            if (!Path.IsPathRooted(scriptPath))
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = "Script path must be an absolute path.",
                });
            }

            var fullPath = Path.GetFullPath(scriptPath);
            if (!global::System.IO.File.Exists(fullPath))
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = $"Script file does not exist: {fullPath}",
                });
            }

            var success = await _customScriptService.ExecuteScriptAsync(fullPath, null, "Test", scriptArgs);
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
            catch (ArgumentException ex)
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = $"Failed to send email test notification: {ex.Message}",
                });
            }
            catch (InvalidOperationException ex)
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = $"Failed to send email test notification: {ex.Message}",
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

            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = "Target webhook URL is required.",
                });
            }

            if (!WebhookDispatcher.IsValidTargetUrl(targetUrl))
            {
                return Ok(new NotificationTestResult
                {
                    Success = false,
                    Message = $"Target URL '{targetUrl}' is prohibited (SSRF protection: loopback, link-local, and cloud metadata addresses are not permitted).",
                });
            }

            var httpMethod = NotificationEventHandler.ResolveHttpMethod(notif.Implementation, notif.Settings);
            var result = httpMethod == HttpMethod.Put
                ? await _webhookDispatcher.DispatchDetailedAsync(targetUrl, payload, customHeaders, httpMethod)
                : await _webhookDispatcher.DispatchDetailedAsync(targetUrl, payload, customHeaders);
            return Ok(new NotificationTestResult
            {
                Success = result.Success,
                Message = result.Message,
                StatusCode = (int?)result.StatusCode,
                ResponseBodySnippet = result.ResponseBodySnippet,
            });
        }
    }

    public static NotificationResource ToResource(NotificationDefinition n)
    {
        if (n == null)
        {
            return null;
        }

        return new NotificationResource
        {
            Id = n.Id,
            Name = n.Name,
            Implementation = n.Implementation,
            ConfigContract = n.ConfigContract,
            Settings = MaskSettings(n.Settings, n.Implementation),
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
            OnBackupComplete = n.OnBackupComplete,
            OnBackupFailed = n.OnBackupFailed,
            FallbackNotificationId = n.FallbackNotificationId,
            Tags = n.Tags != null ? new List<int>(n.Tags) : new List<int>(),
            Categories = n.Categories != null ? new List<string>(n.Categories) : new List<string>(),
        };
    }

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "pass",
        "token",
        "bottoken",
        "bot_token",
        "apptoken",
        "app_token",
        "apikey",
        "api_key",
        "userkey",
        "user_key",
        "secret",
    };

    private static readonly Regex DiscordWebhookRegex = new(
        @"(https?://(?:[a-zA-Z0-9-]+\.)?discord(?:app)?\.com/api/webhooks/\d+/)[^/?#\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SlackWebhookRegex = new(
        @"(https?://hooks\.slack\.com/services/[A-Za-z0-9]+/[A-Za-z0-9]+/)[^/?#\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex QueryParamSecretRegex = new(
        @"((?:^|[?&])(?:token|botToken|bot_token|apiKey|api_key|appToken|app_token|key|secret)=)[^&#\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex QueryParamPasswordRegex = new(
        @"((?:^|[?&])(?:password|pass|userKey|user_key)=)[^&#\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UrlBasicAuthRegex = new(
        @"(https?://[^:/@\s]+:)([^@/\s]+)(@)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string MaskSettings(string settings, string implementation = null)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return settings;
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                var node = JsonNode.Parse(trimmed);
                if (node is JsonObject obj)
                {
                    MaskJsonObject(obj, implementation);
                    return obj.ToJsonString();
                }
            }
            catch
            {
                // Fallback to string masking
            }
        }

        return MaskUrl(trimmed);
    }

    public static string MaskUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var result = DiscordWebhookRegex.Replace(url, $"${{1}}{PasswordMask}");
        result = SlackWebhookRegex.Replace(result, $"${{1}}{PasswordMask}");
        result = QueryParamSecretRegex.Replace(result, $"${{1}}{PasswordMask}");
        result = QueryParamPasswordRegex.Replace(result, $"${{1}}{PasswordMask}");
        result = UrlBasicAuthRegex.Replace(result, $"${{1}}{PasswordMask}${{3}}");
        return result;
    }

    public static bool SettingsContainMaskedSecrets(string settings, string implementation = null)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return false;
        }

        var trimmed = settings.Trim();
        if (trimmed == PasswordMask)
        {
            return true;
        }

        if (trimmed.StartsWith('{'))
        {
            try
            {
                var node = JsonNode.Parse(trimmed);
                if (node is JsonObject obj)
                {
                    return JsonObjectContainsMaskedSecrets(obj, implementation);
                }
            }
            catch
            {
                // Fallback to string scan
            }
        }

        return trimmed.Contains(PasswordMask, StringComparison.Ordinal) || trimmed.Contains('*');
    }

    private static bool JsonObjectContainsMaskedSecrets(JsonObject obj, string implementation)
    {
        var isPushover = string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase);

        foreach (var (key, valueNode) in obj)
        {
            if (valueNode == null)
            {
                continue;
            }

            if (valueNode is JsonObject childObj)
            {
                if (JsonObjectContainsMaskedSecrets(childObj, implementation))
                {
                    return true;
                }

                continue;
            }

            if (valueNode is JsonArray childArr)
            {
                foreach (var element in childArr)
                {
                    if (element is JsonObject elementObj && JsonObjectContainsMaskedSecrets(elementObj, implementation))
                    {
                        return true;
                    }
                }

                continue;
            }

            if (valueNode is JsonValue)
            {
                var strValue = valueNode.ToString();
                if (string.IsNullOrEmpty(strValue))
                {
                    continue;
                }

                if (IsSensitiveKey(key, isPushover))
                {
                    if (strValue == PasswordMask || strValue.Contains('*'))
                    {
                        return true;
                    }
                }
                else if (strValue.Contains('*'))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static string RestoreSecrets(string incomingSettings, string existingSettings, string implementation = null)
    {
        if (string.IsNullOrWhiteSpace(existingSettings))
        {
            return incomingSettings;
        }

        if (string.IsNullOrWhiteSpace(incomingSettings))
        {
            return existingSettings;
        }

        var trimmedIncoming = incomingSettings.Trim();
        var trimmedExisting = existingSettings.Trim();

        if (trimmedIncoming == PasswordMask)
        {
            return existingSettings;
        }

        var isIncomingJson = trimmedIncoming.StartsWith('{');
        var isExistingJson = trimmedExisting.StartsWith('{');

        if (isIncomingJson && isExistingJson)
        {
            try
            {
                var incomingNode = JsonNode.Parse(trimmedIncoming);
                var existingNode = JsonNode.Parse(trimmedExisting);

                if (incomingNode is JsonObject incomingObj && existingNode is JsonObject existingObj)
                {
                    RestoreJsonObjectSecrets(incomingObj, existingObj, implementation);
                    return incomingObj.ToJsonString();
                }
            }
            catch
            {
                // Fallback to URL restore if JSON parsing fails
            }
        }

        return RestoreUrl(trimmedIncoming, trimmedExisting);
    }

    public static string RestoreUrl(string incomingUrl, string existingUrl)
    {
        if (string.IsNullOrWhiteSpace(incomingUrl) || incomingUrl == PasswordMask)
        {
            return existingUrl;
        }

        if (string.IsNullOrWhiteSpace(existingUrl) || !incomingUrl.Contains('*'))
        {
            return incomingUrl;
        }

        if (incomingUrl == MaskUrl(existingUrl))
        {
            return existingUrl;
        }

        var result = incomingUrl;

        // Discord webhook URL restoration
        var existingDiscord = DiscordWebhookRegex.Match(existingUrl);
        var incomingDiscord = DiscordWebhookRegex.Match(result);
        if (existingDiscord.Success && incomingDiscord.Success)
        {
            var prefix = existingDiscord.Groups[1].Value;
            var incomingPrefix = incomingDiscord.Groups[1].Value;
            if (string.Equals(prefix, incomingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var existingToken = existingUrl.Substring(existingDiscord.Groups[1].Length).Split('?', '#', '/')[0];
                var incomingSuffix = result.Substring(incomingDiscord.Index + incomingDiscord.Length);
                result = string.Concat(result.AsSpan(0, incomingDiscord.Groups[1].Length), existingToken, incomingSuffix);
            }
        }

        // Slack webhook URL restoration
        var existingSlack = SlackWebhookRegex.Match(existingUrl);
        var incomingSlack = SlackWebhookRegex.Match(result);
        if (existingSlack.Success && incomingSlack.Success)
        {
            var prefix = existingSlack.Groups[1].Value;
            var incomingPrefix = incomingSlack.Groups[1].Value;
            if (string.Equals(prefix, incomingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var existingToken = existingUrl.Substring(existingSlack.Groups[1].Length).Split('?', '#', '/')[0];
                var incomingSuffix = result.Substring(incomingSlack.Index + incomingSlack.Length);
                result = string.Concat(result.AsSpan(0, incomingSlack.Groups[1].Length), existingToken, incomingSuffix);
            }
        }

        result = RestoreMaskedQueryParams(result, existingUrl, QueryParamSecretRegex);
        result = RestoreMaskedQueryParams(result, existingUrl, QueryParamPasswordRegex);

        // Basic Auth restoration
        var existingAuth = UrlBasicAuthRegex.Match(existingUrl);
        var incomingAuth = UrlBasicAuthRegex.Match(result);
        if (existingAuth.Success && incomingAuth.Success)
        {
            var existingPass = existingAuth.Groups[2].Value;
            var incomingSuffix = result.Substring(incomingAuth.Groups[1].Length + incomingAuth.Groups[2].Length);
            result = string.Concat(result.AsSpan(0, incomingAuth.Groups[1].Length), existingPass, incomingSuffix);
        }

        return result;
    }

    private static string RestoreMaskedQueryParams(string incomingUrl, string existingUrl, Regex regex)
    {
        var result = incomingUrl;
        var scanIndex = 0;
        while (scanIndex < result.Length)
        {
            var incomingParams = regex.Match(result, scanIndex);
            if (!incomingParams.Success)
            {
                break;
            }

            var incomingPrefix = incomingParams.Groups[1].Value;
            var existingParams = regex.Match(existingUrl);
            var restored = false;
            while (existingParams.Success)
            {
                if (string.Equals(existingParams.Groups[1].Value, incomingPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var existingToken = existingUrl.Substring(existingParams.Index + existingParams.Groups[1].Length).Split('&', '#')[0];
                    var valueStart = incomingParams.Index + incomingParams.Groups[1].Length;
                    var incomingSuffix = result.Substring(incomingParams.Index + incomingParams.Length);
                    result = string.Concat(result.AsSpan(0, valueStart), existingToken, incomingSuffix);
                    scanIndex = valueStart + existingToken.Length;
                    restored = true;
                    break;
                }

                existingParams = existingParams.NextMatch();
            }

            if (!restored)
            {
                scanIndex = incomingParams.Index + incomingParams.Length;
            }
        }

        return result;
    }

    private static void MaskJsonObject(JsonObject obj, string implementation)
    {
        var isPushover = string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase);
        var keys = obj.Select(kvp => kvp.Key).ToList();

        foreach (var key in keys)
        {
            var valueNode = obj[key];
            if (valueNode == null)
            {
                continue;
            }

            if (valueNode is JsonObject childObj)
            {
                MaskJsonObject(childObj, implementation);
                continue;
            }

            if (valueNode is JsonArray childArr)
            {
                foreach (var element in childArr)
                {
                    if (element is JsonObject elementObj)
                    {
                        MaskJsonObject(elementObj, implementation);
                    }
                }

                continue;
            }

            if (valueNode is JsonValue)
            {
                var strValue = valueNode.ToString();
                if (string.IsNullOrEmpty(strValue))
                {
                    continue;
                }

                if (IsSensitiveKey(key, isPushover))
                {
                    obj[key] = PasswordMask;
                }
                else
                {
                    var maskedUrl = MaskUrl(strValue);
                    if (maskedUrl != strValue)
                    {
                        obj[key] = maskedUrl;
                    }
                }
            }
        }
    }

    private static void RestoreJsonObjectSecrets(JsonObject incomingObj, JsonObject existingObj, string implementation)
    {
        var isPushover = string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase);

        foreach (var (key, existingVal) in existingObj)
        {
            if (existingVal == null)
            {
                continue;
            }

            if (existingVal is JsonObject existingChildObj)
            {
                if (incomingObj.TryGetPropertyValue(key, out var incomingVal) && incomingVal is JsonObject incomingChildObj)
                {
                    RestoreJsonObjectSecrets(incomingChildObj, existingChildObj, implementation);
                }
                else if (incomingVal == null)
                {
                    incomingObj[key] = existingChildObj.DeepClone();
                }

                continue;
            }

            var existingStr = existingVal.ToString();
            if (string.IsNullOrEmpty(existingStr))
            {
                continue;
            }

            if (IsSensitiveKey(key, isPushover))
            {
                if (!incomingObj.TryGetPropertyValue(key, out var incomingVal) || incomingVal == null)
                {
                    incomingObj[key] = existingVal.DeepClone();
                }
                else
                {
                    var incomingStr = incomingVal.ToString();
                    if (string.IsNullOrWhiteSpace(incomingStr) || incomingStr == PasswordMask || incomingStr.Contains('*'))
                    {
                        incomingObj[key] = existingVal.DeepClone();
                    }
                }
            }
            else
            {
                if (incomingObj.TryGetPropertyValue(key, out var incomingVal) && incomingVal != null)
                {
                    var incomingStr = incomingVal.ToString();
                    if (!string.IsNullOrEmpty(incomingStr) && (incomingStr.Contains('*') || incomingStr == PasswordMask))
                    {
                        var restoredUrl = RestoreUrl(incomingStr, existingStr);
                        if (restoredUrl != incomingStr)
                        {
                            incomingObj[key] = restoredUrl;
                        }
                    }
                }
            }
        }
    }

    private static bool IsSensitiveKey(string key, bool isPushover)
    {
        if (SensitiveKeys.Contains(key))
        {
            return true;
        }

        if (isPushover && (string.Equals(key, "user", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "userKey", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "user_key", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
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
            OnBackupComplete = r.OnBackupComplete,
            OnBackupFailed = r.OnBackupFailed,
            FallbackNotificationId = r.FallbackNotificationId,
            Tags = r.Tags ?? new List<int>(),
            Categories = r.Categories ?? new List<string>(),
        };
    }

    private string ValidateFallbackNotificationId(NotificationResource resource, int? notificationId = null)
    {
        if (!resource.FallbackNotificationId.HasValue)
        {
            return null;
        }

        var fallbackId = resource.FallbackNotificationId.Value;
        if (fallbackId <= 0)
        {
            return "Fallback notification ID must be a positive integer.";
        }

        var selfId = notificationId ?? (resource.Id > 0 ? resource.Id : (int?)null);
        if (selfId.HasValue && fallbackId == selfId.Value)
        {
            return "A notification cannot use itself as a fallback notification.";
        }

        if (_notificationRepository.Get(fallbackId) == null)
        {
            return $"Fallback notification with ID {fallbackId} was not found.";
        }

        return null;
    }

    private static bool HasActiveTrigger(NotificationResource resource)
    {
        return resource.OnGrab ||
            resource.OnDownloadComplete ||
            resource.OnMediaInspected ||
            resource.OnExtractComplete ||
            resource.OnSeedGoalReached ||
            resource.OnTorrentDeleted ||
            resource.OnHealthIssue ||
            resource.OnHealthRestored ||
            resource.OnManualInteractionRequired ||
            resource.OnApplicationUpdate ||
            resource.OnBackupComplete ||
            resource.OnBackupFailed;
    }

    private static bool HasActiveTrigger(NotificationDefinition definition)
    {
        return definition.OnGrab ||
            definition.OnDownloadComplete ||
            definition.OnMediaInspected ||
            definition.OnExtractComplete ||
            definition.OnSeedGoalReached ||
            definition.OnTorrentDeleted ||
            definition.OnHealthIssue ||
            definition.OnHealthRestored ||
            definition.OnManualInteractionRequired ||
            definition.OnApplicationUpdate ||
            definition.OnBackupComplete ||
            definition.OnBackupFailed;
    }

    private static string ExtractSetting(string settings, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return string.Empty;
        }

        if (settings.TrimStart().StartsWith('{'))
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
            catch (JsonException ex)
            {
                _logger.Trace(ex, "Settings payload is not JSON; falling back to regex matching");
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
