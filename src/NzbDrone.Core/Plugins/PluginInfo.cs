using System;

namespace NzbDrone.Core.Plugins;

public class PluginInfo
{
    public PluginManifest Manifest { get; set; }
    public PluginState State { get; set; }
    public bool Enabled { get; set; }
    public int? ProcessId { get; set; }
    public int CrashCount { get; set; }
    public string LastError { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? LastCrashTime { get; set; }
    public string PluginDirectory { get; set; }
}
