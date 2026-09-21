using System;
using System.IO;
using System.Security;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.Plugins;

public class PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; }

    [JsonPropertyName("entrypoint")]
    public string Entrypoint { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "sidecar";

    [JsonPropertyName("capabilities")]
    public string[] Capabilities { get; set; } = Array.Empty<string>();

    [JsonPropertyName("settingsSchema")]
    public string SettingsSchema { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("Plugin Id is required.", nameof(Id));
        }

        if (Id.Contains("..") || Id.Contains('/') || Id.Contains('\\') || Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new SecurityException($"Plugin Id '{Id}' contains invalid characters or directory traversal sequences.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Plugin Name is required.", nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new ArgumentException("Plugin Version is required.", nameof(Version));
        }

        if (string.IsNullOrWhiteSpace(Entrypoint))
        {
            throw new ArgumentException("Plugin Entrypoint is required.", nameof(Entrypoint));
        }

        if (Entrypoint.Contains(".."))
        {
            throw new SecurityException($"Entrypoint '{Entrypoint}' contains forbidden path traversal sequence '..'.");
        }
    }

    public string ValidateAndResolveEntrypoint(string pluginDirectory)
    {
        Validate();

        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            throw new ArgumentException("Plugin directory cannot be null or empty.", nameof(pluginDirectory));
        }

        var fullPluginDir = Path.GetFullPath(pluginDirectory);
        if (!fullPluginDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            fullPluginDir += Path.DirectorySeparatorChar;
        }

        string fullEntrypoint;
        if (Path.IsPathRooted(Entrypoint))
        {
            fullEntrypoint = Path.GetFullPath(Entrypoint);
        }
        else
        {
            fullEntrypoint = Path.GetFullPath(Path.Combine(pluginDirectory, Entrypoint));
        }

        if (!fullEntrypoint.StartsWith(fullPluginDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Plugin Entrypoint '{Entrypoint}' resolves to '{fullEntrypoint}', which is outside the plugin directory '{fullPluginDir}'.");
        }

        return fullEntrypoint;
    }
}
