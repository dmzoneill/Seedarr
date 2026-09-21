using System;
using NzbDrone.Core.Plugins;

namespace Seedarr.Api.V1.Plugins;

public static class PluginResourceMapper
{
    public static PluginResource ToResource(PluginInfo info)
    {
        if (info == null)
        {
            return null;
        }

        return new PluginResource
        {
            Id = info.Manifest?.Id,
            Name = info.Manifest?.Name,
            Version = info.Manifest?.Version,
            Description = info.Manifest?.Description,
            Author = info.Manifest?.Author,
            Entrypoint = info.Manifest?.Entrypoint,
            Type = info.Manifest?.Type ?? "sidecar",
            Capabilities = info.Manifest?.Capabilities ?? Array.Empty<string>(),
            SettingsSchema = info.Manifest?.SettingsSchema,
            Status = info.State.ToString(),
            Enabled = info.Enabled,
            ProcessId = info.ProcessId,
            CrashCount = info.CrashCount,
            LastError = info.LastError
        };
    }
}
