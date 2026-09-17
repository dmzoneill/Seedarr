#nullable enable
using System.Collections.Generic;

namespace NzbDrone.Core.Automation;

public class NotificationActionPayload
{
    public string? Provider { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public class ArrSyncPayload
{
    public string? AppType { get; set; }

    public int? InstanceId { get; set; }
}

public class CustomScriptPayload
{
    public string Path { get; set; } = string.Empty;

    public List<string> Arguments { get; set; } = new();

    public int TimeoutSeconds { get; set; } = 60;
}

public class LinkPayload
{
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool IsHardlink { get; set; }
}

public class PermissionsPayload
{
    public string Path { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public bool Recurse { get; set; }
}

public class DiscordWebhookPayload
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public Dictionary<string, string> Fields { get; set; } = new();
}

public class TelegramPayload
{
    public string Token { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ParseMode { get; set; } = "Markdown";
}

public class NtfyPayload
{
    public string Server { get; set; } = "https://ntfy.sh";
    public string Topic { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Click { get; set; } = string.Empty;
}

public class PushoverPayload
{
    public string Token { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Sound { get; set; } = string.Empty;
}

public class AutomationExecutionResult
{
    public bool Success { get; set; }

    public string? OutputLog { get; set; }

    public string? Error { get; set; }

    public List<string> Warnings { get; set; } = new();

    public long ExecutionTimeMs { get; set; }

    public List<string> TagsToAdd { get; set; } = new();

    public List<string> TagsToRemove { get; set; } = new();

    public string? NewCategory { get; set; }

    public bool ShouldPause { get; set; }

    public bool ShouldResume { get; set; }

    public bool ShouldRemove { get; set; }

    public bool DeleteDataOnRemove { get; set; }

    public int? NewUploadLimitKbps { get; set; }

    public int? NewDownloadLimitKbps { get; set; }

    public double? NewRatioLimit { get; set; }

    public int? NewSeedingTimeLimitMinutes { get; set; }

    public int? NewPriority { get; set; }

    public bool? NewSequentialDownload { get; set; }

    public bool? NewSuperSeeding { get; set; }

    public string? NewSavePath { get; set; }

    public bool ShouldRecheck { get; set; }

    public bool ShouldReannounce { get; set; }

    public bool ShouldBoostTracker { get; set; }

    public List<string> TrackersToAdd { get; set; } = new();

    public List<string> TrackersToRemove { get; set; } = new();

    public List<string> PeersToBan { get; set; } = new();

    public List<NotificationActionPayload> NotificationsToSend { get; set; } = new();

    public List<ArrSyncPayload> ArrSyncsToSend { get; set; } = new();

    public List<CustomScriptPayload> ScriptsToRun { get; set; } = new();

    public bool ShouldExtractArchive { get; set; }

    public string? ExtractDestination { get; set; }

    public bool DeleteArchiveOnExtract { get; set; }

    public List<string> CleanFilePatterns { get; set; } = new();

    public bool ShouldStopPipeline { get; set; }

    public string? StopReason { get; set; }

    public string? ShareLimitAction { get; set; }

    public Dictionary<string, int> FilePriorities { get; set; } = new();

    public Dictionary<string, string> TrackersToReplace { get; set; } = new();

    public bool ShouldReannounceAll { get; set; }

    public string? ExportTorrentDestination { get; set; }

    public List<LinkPayload> LinksToCreate { get; set; } = new();

    public List<PermissionsPayload> PermissionsToSet { get; set; } = new();

    public List<DiscordWebhookPayload> DiscordWebhooksToSend { get; set; } = new();

    public List<TelegramPayload> TelegramMessagesToSend { get; set; } = new();

    public List<NtfyPayload> NtfyMessagesToSend { get; set; } = new();

    public List<PushoverPayload> PushoverMessagesToSend { get; set; } = new();

    public List<string> PipelinesToInvoke { get; set; } = new();
}
