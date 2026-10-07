#nullable enable
using System;
using System.Collections.Generic;
using NzbDrone.Core.Automation;

namespace Seedarr.Api.V1.Automation;

internal static class AutomationScriptUpdateMerger
{
    public static AutomationScript Merge(
        AutomationScript existing,
        AutomationScriptResource resource,
        IReadOnlySet<string> presentPropertyKeys)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(resource);

        return new AutomationScript
        {
            Id = existing.Id,
            Name = Present(presentPropertyKeys, "name") ? resource.Name ?? string.Empty : existing.Name,
            Description = Present(presentPropertyKeys, "description") ? resource.Description : existing.Description,
            Trigger = Present(presentPropertyKeys, "trigger") ? resource.Trigger : existing.Trigger,
            Language = Present(presentPropertyKeys, "language") ? resource.Language : existing.Language,
            Code = Present(presentPropertyKeys, "code") ? resource.Code ?? string.Empty : existing.Code,
            InputsJson = Present(presentPropertyKeys, "inputsJson") ? resource.InputsJson : existing.InputsJson,
            IsEnabled = Present(presentPropertyKeys, "isEnabled") ? resource.IsEnabled : existing.IsEnabled,
            TargetCategories = Present(presentPropertyKeys, "targetCategories")
                ? resource.TargetCategories ?? new List<string>()
                : new List<string>(existing.TargetCategories ?? new List<string>()),
            TargetTagIds = Present(presentPropertyKeys, "targetTagIds")
                ? resource.TargetTagIds ?? new List<int>()
                : new List<int>(existing.TargetTagIds ?? new List<int>()),
            CreatedAt = Present(presentPropertyKeys, "createdAt") ? resource.CreatedAt : existing.CreatedAt,
            LastExecutedAt = existing.LastExecutedAt,
            LastExecutionStatus = existing.LastExecutionStatus,
            LastExecutionLog = existing.LastExecutionLog,
        };
    }

    private static bool Present(IReadOnlySet<string> presentPropertyKeys, string camelCaseName)
    {
        return presentPropertyKeys.Contains(camelCaseName);
    }
}
