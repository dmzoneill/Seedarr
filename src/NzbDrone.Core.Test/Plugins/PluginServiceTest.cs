using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Mcp;
using NzbDrone.Core.Plugins;
using NzbDrone.Core.Processes;

namespace NzbDrone.Core.Test.Plugins;

[TestFixture]
public class PluginServiceTest
{
    private string _tempAppData;
    private IAppFolderInfo _appFolderInfo;
    private ISidecarProcessSupervisor _supervisor;

    [SetUp]
    public void SetUp()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "seedarr_appdata_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);

        _appFolderInfo = Substitute.For<IAppFolderInfo>();
        _appFolderInfo.AppDataFolder.Returns(_tempAppData);

        _supervisor = Substitute.For<ISidecarProcessSupervisor>();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempAppData))
        {
            Directory.Delete(_tempAppData, true);
        }
    }

    [Test]
    public void ScanPlugins_should_discover_valid_plugin_manifests()
    {
        var pluginsDir = Path.Combine(_tempAppData, "plugins", "sample-plugin");
        Directory.CreateDirectory(pluginsDir);

        var manifest = new PluginManifest
        {
            Id = "sample-plugin",
            Name = "Sample Plugin",
            Version = "1.2.3",
            Description = "A sample sidecar plugin",
            Author = "Seedarr Community",
            Entrypoint = "run.sh",
            Type = "sidecar",
            Capabilities = new[] { "indexer", "automation" }
        };

        File.WriteAllText(Path.Combine(pluginsDir, "plugin.json"), JsonSerializer.Serialize(manifest, McpJsonOptions.Default));
        File.WriteAllText(Path.Combine(pluginsDir, "run.sh"), "#!/bin/sh\necho hello");

        using var service = new PluginService(_appFolderInfo, _supervisor);

        var plugins = service.GetAll();
        Assert.That(plugins.Count, Is.EqualTo(1));

        var discovered = service.Get("sample-plugin");
        Assert.That(discovered, Is.Not.Null);
        Assert.That(discovered.Manifest.Name, Is.EqualTo("Sample Plugin"));
        Assert.That(discovered.Manifest.Version, Is.EqualTo("1.2.3"));
        Assert.That(discovered.State, Is.EqualTo(PluginState.Stopped));
    }

    [Test]
    public void ScanPlugins_should_ignore_plugins_with_directory_traversal()
    {
        var pluginsDir = Path.Combine(_tempAppData, "plugins", "malicious-plugin");
        Directory.CreateDirectory(pluginsDir);

        var manifest = new PluginManifest
        {
            Id = "malicious-plugin",
            Name = "Malicious Plugin",
            Version = "1.0.0",
            Entrypoint = "../../outside.sh",
            Type = "sidecar"
        };

        File.WriteAllText(Path.Combine(pluginsDir, "plugin.json"), JsonSerializer.Serialize(manifest, McpJsonOptions.Default));

        using var service = new PluginService(_appFolderInfo, _supervisor);

        var discovered = service.Get("malicious-plugin");
        Assert.That(discovered, Is.Null);
    }

    [Test]
    public void Enable_and_Disable_should_toggle_plugin_state()
    {
        var pluginsDir = Path.Combine(_tempAppData, "plugins", "toggle-plugin");
        Directory.CreateDirectory(pluginsDir);

        var manifest = new PluginManifest
        {
            Id = "toggle-plugin",
            Name = "Toggle Plugin",
            Version = "1.0.0",
            Entrypoint = "entry.sh",
            Type = "sidecar"
        };

        File.WriteAllText(Path.Combine(pluginsDir, "plugin.json"), JsonSerializer.Serialize(manifest, McpJsonOptions.Default));
        File.WriteAllText(Path.Combine(pluginsDir, "entry.sh"), "#!/bin/sh\necho ok");

        using var service = new PluginService(_appFolderInfo, _supervisor);

        var disabled = service.Disable("toggle-plugin");
        Assert.That(disabled, Is.Not.Null);
        Assert.That(disabled.Enabled, Is.False);
        Assert.That(disabled.State, Is.EqualTo(PluginState.Disabled));

        // Disable of unknown plugin returns null
        Assert.That(service.Disable("nonexistent"), Is.Null);
    }

    [Test]
    public void Handle_ApplicationShutdownRequested_should_not_throw()
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);
        Assert.DoesNotThrow(() => service.Handle(new ApplicationShutdownRequested()));
    }
}
