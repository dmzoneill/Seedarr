using System;

namespace Seedarr.Api.V1.Plugins;

public class PluginResource
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public string Entrypoint { get; set; }
    public string Type { get; set; }
    public string[] Capabilities { get; set; } = Array.Empty<string>();
    public string SettingsSchema { get; set; }
    public string Status { get; set; }
    public bool Enabled { get; set; }
    public int? ProcessId { get; set; }
    public int CrashCount { get; set; }
    public string LastError { get; set; }
}
