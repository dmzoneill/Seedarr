#nullable enable
using System;
using System.Collections.Generic;
using NzbDrone.Core.Notifications;

namespace Seedarr.Api.V1.Notifications;

internal static class NotificationUpdateMerger
{
    public static NotificationDefinition Merge(
        NotificationDefinition existing,
        NotificationResource incoming,
        IReadOnlySet<string> presentPropertyKeys)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var merged = new NotificationDefinition
        {
            Id = existing.Id,
            Name = Present(presentPropertyKeys, "name") ? incoming.Name : existing.Name,
            Implementation = Present(presentPropertyKeys, "implementation")
                ? incoming.Implementation ?? existing.Implementation ?? "Webhook"
                : existing.Implementation,
            ConfigContract = Present(presentPropertyKeys, "configContract")
                ? incoming.ConfigContract
                : existing.ConfigContract,
            Settings = Present(presentPropertyKeys, "settings") ? incoming.Settings : existing.Settings,
            Enable = Present(presentPropertyKeys, "enable") ? incoming.Enable : existing.Enable,
            OnGrab = Present(presentPropertyKeys, "onGrab") ? incoming.OnGrab : existing.OnGrab,
            OnDownloadComplete = Present(presentPropertyKeys, "onDownloadComplete")
                ? incoming.OnDownloadComplete
                : existing.OnDownloadComplete,
            OnMediaInspected = Present(presentPropertyKeys, "onMediaInspected")
                ? incoming.OnMediaInspected
                : existing.OnMediaInspected,
            OnExtractComplete = Present(presentPropertyKeys, "onExtractComplete")
                ? incoming.OnExtractComplete
                : existing.OnExtractComplete,
            OnSeedGoalReached = Present(presentPropertyKeys, "onSeedGoalReached")
                ? incoming.OnSeedGoalReached
                : existing.OnSeedGoalReached,
            OnTorrentDeleted = Present(presentPropertyKeys, "onTorrentDeleted")
                ? incoming.OnTorrentDeleted
                : existing.OnTorrentDeleted,
            OnHealthIssue = Present(presentPropertyKeys, "onHealthIssue") ? incoming.OnHealthIssue : existing.OnHealthIssue,
            OnHealthRestored = Present(presentPropertyKeys, "onHealthRestored")
                ? incoming.OnHealthRestored
                : existing.OnHealthRestored,
            OnManualInteractionRequired = Present(presentPropertyKeys, "onManualInteractionRequired")
                ? incoming.OnManualInteractionRequired
                : existing.OnManualInteractionRequired,
            OnApplicationUpdate = Present(presentPropertyKeys, "onApplicationUpdate")
                ? incoming.OnApplicationUpdate
                : existing.OnApplicationUpdate,
            OnBackupComplete = Present(presentPropertyKeys, "onBackupComplete")
                ? incoming.OnBackupComplete
                : existing.OnBackupComplete,
            OnBackupFailed = Present(presentPropertyKeys, "onBackupFailed")
                ? incoming.OnBackupFailed
                : existing.OnBackupFailed,
            FallbackNotificationId = Present(presentPropertyKeys, "fallbackNotificationId")
                ? incoming.FallbackNotificationId
                : existing.FallbackNotificationId,
            Tags = Present(presentPropertyKeys, "tags")
                ? incoming.Tags ?? new List<int>()
                : new List<int>(existing.Tags ?? new List<int>()),
            Categories = Present(presentPropertyKeys, "categories")
                ? incoming.Categories ?? new List<string>()
                : new List<string>(existing.Categories ?? new List<string>()),
        };

        return merged;
    }

    private static bool Present(IReadOnlySet<string> presentPropertyKeys, string camelCaseName)
    {
        return presentPropertyKeys.Contains(camelCaseName);
    }
}
