#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Automation;

#pragma warning disable SA1300 // Element should begin with upper-case letter (DSL wrapper)
public class ScriptSystemContext
{
    private readonly IManageCommandQueue? _commandQueue;
    private readonly AutomationExecutionResult? _result;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public long diskFreeSpace { get; set; }
    public bool vpnActive { get; set; } = true;
    public bool isPortForwarded { get; set; } = true;

    public ScriptSystemContext(IManageCommandQueue? commandQueue = null, AutomationExecutionResult? result = null, long freeSpace = 0, bool isVpnActive = true, bool isPortForward = true)
    {
        _commandQueue = commandQueue;
        _result = result;
        diskFreeSpace = freeSpace;
        vpnActive = isVpnActive;
        isPortForwarded = isPortForward;
    }

    public object? runCommand(string commandName, object? payload = null)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new ArgumentException("Command name cannot be empty.", nameof(commandName));
        }

        if (_commandQueue == null)
        {
            _logger.Warn("Command queue is not available to execute command {0}", commandName);
            return null;
        }

        var jsonBody = payload != null ? JsonSerializer.Serialize(payload) : "{}";
        var cmdResult = _commandQueue.PushRaw(commandName.Trim(), jsonBody, CommandTrigger.Manual);
        _logger.Info("Queued system command '{0}' (Id={1}) from automation script", commandName, cmdResult?.Id);

        return new
        {
            id = cmdResult?.Id,
            name = cmdResult?.Name,
            status = cmdResult?.Status.ToString(),
        };
    }

    public object? executeTask(string taskName)
    {
        return runCommand(taskName);
    }

    public void sendNotification(string title, string message, string? provider = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _result?.NotificationsToSend.Add(new NotificationActionPayload
        {
            Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim(),
            Title = title ?? string.Empty,
            Message = message ?? string.Empty,
        });

        _logger.Info("Queued notification '{0}' (provider={1}) from automation script", title, provider ?? "all");
    }

    public void notifyArr(string? appType = null, int? instanceId = null)
    {
        _result?.ArrSyncsToSend.Add(new ArrSyncPayload
        {
            AppType = string.IsNullOrWhiteSpace(appType) ? null : appType.Trim(),
            InstanceId = instanceId,
        });

        _logger.Info("Queued Servarr sync (appType={0}, instanceId={1}) from automation script", appType ?? "all", instanceId);
    }

    public void syncArr(string? appType = null)
    {
        notifyArr(appType);
    }

    public void runScript(string path, object? args = null, int timeoutSeconds = 60)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var argList = new List<string>();
        if (args is IEnumerable<object> objList)
        {
            foreach (var item in objList)
            {
                if (item != null)
                {
                    argList.Add(item.ToString()!);
                }
            }
        }
        else if (args is string argStr)
        {
            argList.Add(argStr);
        }

        _result?.ScriptsToRun.Add(new CustomScriptPayload
        {
            Path = path.Trim(),
            Arguments = argList,
            TimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 60,
        });

        _logger.Info("Queued external script execution '{0}' from automation script", path);
    }

    public void delay(int seconds)
    {
        sleep(seconds);
    }

    public void sleep(int seconds)
    {
        var clamped = Math.Clamp(seconds, 1, 30);
        _logger.Info("Automation script sleeping for {0} seconds", clamped);
        Thread.Sleep(clamped * 1000);
    }

    public void log(string message, string level = "info")
    {
        if (string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase) || string.Equals(level, "warning", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warn("[AUTOMATION] {0}", message);
        }
        else if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error("[AUTOMATION] {0}", message);
        }
        else
        {
            _logger.Info("[AUTOMATION] {0}", message);
        }
    }

    public void stopPipeline(string reason = "")
    {
        if (_result != null)
        {
            _result.ShouldStopPipeline = true;
            _result.StopReason = reason;
        }

        _logger.Info("Automation script requested pipeline termination: {0}", reason);
    }

    public void createHardlink(string source, string destination)
    {
        _result?.LinksToCreate.Add(new LinkPayload { Source = source, Destination = destination, IsHardlink = true });
    }

    public void createSymlink(string source, string destination)
    {
        _result?.LinksToCreate.Add(new LinkPayload { Source = source, Destination = destination, IsHardlink = false });
    }

    public void setFilePermissions(string path, string mode, bool recurse = false)
    {
        _result?.PermissionsToSet.Add(new PermissionsPayload { Path = path, Mode = mode, Recurse = recurse });
    }

    public string? calculateChecksum(string path, string algorithm = "SHA256")
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = System.IO.File.OpenRead(path);
            byte[] bytes;
            if (algorithm.Equals("MD5", StringComparison.OrdinalIgnoreCase))
            {
                using var md5 = System.Security.Cryptography.MD5.Create();
                bytes = md5.ComputeHash(stream);
            }
            else
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                bytes = sha.ComputeHash(stream);
            }

            return System.BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to calculate checksum for {0}", path);
            return null;
        }
    }

    public void reannounceAll()
    {
        if (_result != null)
        {
            _result.ShouldReannounceAll = true;
        }
    }

    public void sendDiscordWebhook(string url, string title, string description, string color = "", Dictionary<string, string>? fields = null)
    {
        _result?.DiscordWebhooksToSend.Add(new DiscordWebhookPayload
        {
            Url = url,
            Title = title,
            Description = description,
            Color = color,
            Fields = fields ?? new Dictionary<string, string>()
        });
    }

    public void sendTelegramMessage(string token, string chatId, string message, string parseMode = "Markdown")
    {
        _result?.TelegramMessagesToSend.Add(new TelegramPayload
        {
            Token = token,
            ChatId = chatId,
            Message = message,
            ParseMode = parseMode
        });
    }

    public void sendNtfy(string topic, string message, string title = "", string priority = "", string tags = "", string click = "", string server = "https://ntfy.sh")
    {
        _result?.NtfyMessagesToSend.Add(new NtfyPayload
        {
            Server = server,
            Topic = topic,
            Title = title,
            Message = message,
            Priority = priority,
            Tags = tags,
            Click = click
        });
    }

    public void sendPushover(string token, string user, string message, string priority = "", string sound = "")
    {
        _result?.PushoverMessagesToSend.Add(new PushoverPayload
        {
            Token = token,
            User = user,
            Message = message,
            Priority = priority,
            Sound = sound
        });
    }

    public void invokePipeline(string pipeline)
    {
        if (!string.IsNullOrWhiteSpace(pipeline))
        {
            _result?.PipelinesToInvoke.Add(pipeline);
        }
    }
}
#pragma warning restore SA1300
