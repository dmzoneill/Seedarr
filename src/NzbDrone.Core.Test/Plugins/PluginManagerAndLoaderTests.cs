using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Mcp;
using NzbDrone.Core.Plugins;
using NzbDrone.Core.Processes;

namespace NzbDrone.Core.Test.PluginTests;

[TestFixture]
public class PluginManagerAndLoaderTests
{
    private string _tempAppData;
    private string _pluginsFolder;
    private IAppFolderInfo _appFolderInfo;
    private ISidecarProcessSupervisor _supervisor;

    [SetUp]
    public void SetUp()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "seedarr_plugins_test_" + Guid.NewGuid().ToString("N"));
        _pluginsFolder = Path.Combine(_tempAppData, "plugins");
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
            try
            {
                Directory.Delete(_tempAppData, true);
            }
            catch
            {
                // Best-effort test cleanup
            }
        }
    }

    private string CreatePluginDirectory(string pluginId, PluginManifest manifest = null, string scriptFileName = "run.sh")
    {
        var dir = Path.Combine(_pluginsFolder, pluginId);
        Directory.CreateDirectory(dir);

        var manifestObj = manifest ?? new PluginManifest
        {
            Id = pluginId,
            Name = "Plugin " + pluginId,
            Version = "1.0.0",
            Description = "Description for " + pluginId,
            Author = "Seedarr Author",
            Entrypoint = scriptFileName,
            Type = "sidecar",
            Capabilities = new[] { "indexer", "search" }
        };

        File.WriteAllText(Path.Combine(dir, "plugin.json"), JsonSerializer.Serialize(manifestObj, McpJsonOptions.Default));
        File.WriteAllText(Path.Combine(dir, scriptFileName), "#!/bin/sh\necho running");

        return dir;
    }

    [Test]
    public void Validate_with_valid_manifest_should_succeed()
    {
        var manifest = new PluginManifest
        {
            Id = "valid-plugin-id",
            Name = "Valid Plugin",
            Version = "1.0.0",
            Description = "A valid sidecar plugin",
            Author = "Seedarr Dev",
            Entrypoint = "bin/start.sh",
            Type = "sidecar",
            Capabilities = new[] { "indexer", "torrent_filter" }
        };

        Assert.DoesNotThrow(() => manifest.Validate());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_with_empty_or_whitespace_id_should_throw_ArgumentException(string id)
    {
        var manifest = new PluginManifest
        {
            Id = id,
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = "start.sh"
        };

        var ex = Assert.Throws<ArgumentException>(() => manifest.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("Id"));
    }

    [TestCase("../evil")]
    [TestCase("evil/../../outside")]
    [TestCase("evil\\..\\outside")]
    [TestCase("plugin/sub")]
    [TestCase("plugin\\sub")]
    public void Validate_with_directory_traversal_in_id_should_throw_SecurityException(string invalidId)
    {
        var manifest = new PluginManifest
        {
            Id = invalidId,
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = "start.sh"
        };

        Assert.Throws<SecurityException>(() => manifest.Validate());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_with_empty_or_whitespace_name_should_throw_ArgumentException(string name)
    {
        var manifest = new PluginManifest
        {
            Id = "valid-id",
            Name = name,
            Version = "1.0.0",
            Entrypoint = "start.sh"
        };

        var ex = Assert.Throws<ArgumentException>(() => manifest.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("Name"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_with_empty_or_whitespace_version_should_throw_ArgumentException(string version)
    {
        var manifest = new PluginManifest
        {
            Id = "valid-id",
            Name = "Test",
            Version = version,
            Entrypoint = "start.sh"
        };

        var ex = Assert.Throws<ArgumentException>(() => manifest.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("Version"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_with_empty_or_whitespace_entrypoint_should_throw_ArgumentException(string entrypoint)
    {
        var manifest = new PluginManifest
        {
            Id = "valid-id",
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = entrypoint
        };

        var ex = Assert.Throws<ArgumentException>(() => manifest.Validate());
        Assert.That(ex.ParamName, Is.EqualTo("Entrypoint"));
    }

    [TestCase("../run.sh")]
    [TestCase("bin/../../run.sh")]
    public void Validate_with_traversal_in_entrypoint_should_throw_SecurityException(string entrypoint)
    {
        var manifest = new PluginManifest
        {
            Id = "valid-id",
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = entrypoint
        };

        Assert.Throws<SecurityException>(() => manifest.Validate());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ValidateAndResolveEntrypoint_with_null_or_whitespace_plugin_dir_should_throw_ArgumentException(string dir)
    {
        var manifest = new PluginManifest
        {
            Id = "valid-id",
            Name = "Test",
            Version = "1.0.0",
            Entrypoint = "run.sh"
        };

        Assert.Throws<ArgumentException>(() => manifest.ValidateAndResolveEntrypoint(dir));
    }

    [Test]
    public void ValidateAndResolveEntrypoint_with_relative_path_should_resolve_under_plugin_dir()
    {
        var manifest = new PluginManifest
        {
            Id = "sample",
            Name = "Sample",
            Version = "1.0.0",
            Entrypoint = "scripts/main.sh"
        };

        var resolved = manifest.ValidateAndResolveEntrypoint(_tempAppData);
        var expected = Path.GetFullPath(Path.Combine(_tempAppData, "scripts", "main.sh"));

        Assert.That(resolved, Is.EqualTo(expected));
    }

    [Test]
    public void ValidateAndResolveEntrypoint_with_rooted_path_outside_plugin_dir_should_throw_SecurityException()
    {
        var manifest = new PluginManifest
        {
            Id = "sample",
            Name = "Sample",
            Version = "1.0.0",
            Entrypoint = Path.Combine(Path.GetTempPath(), "malicious_script.sh")
        };

        var targetDir = Path.Combine(_tempAppData, "subfolder");
        Directory.CreateDirectory(targetDir);

        Assert.Throws<SecurityException>(() => manifest.ValidateAndResolveEntrypoint(targetDir));
    }

    [Test]
    public void ValidateAndResolveEntrypoint_with_rooted_path_inside_plugin_dir_should_succeed()
    {
        var targetDir = Path.Combine(_tempAppData, "plugin_sub");
        Directory.CreateDirectory(targetDir);

        var insidePath = Path.Combine(targetDir, "run.sh");
        var manifest = new PluginManifest
        {
            Id = "sample",
            Name = "Sample",
            Version = "1.0.0",
            Entrypoint = insidePath
        };

        var resolved = manifest.ValidateAndResolveEntrypoint(targetDir);
        Assert.That(resolved, Is.EqualTo(Path.GetFullPath(insidePath)));
    }

    [Test]
    public void ScanPlugins_when_appdata_folder_is_null_or_empty_should_do_nothing()
    {
        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns((string)null);

        using var service = new PluginService(appFolderInfo, _supervisor);

        Assert.That(service.GetAll(), Is.Empty);
    }

    [Test]
    public void ScanPlugins_when_plugins_directory_does_not_exist_should_create_it()
    {
        var pluginsDir = Path.Combine(_tempAppData, "plugins");
        if (Directory.Exists(pluginsDir))
        {
            Directory.Delete(pluginsDir, true);
        }

        using var service = new PluginService(_appFolderInfo, _supervisor);

        Assert.That(Directory.Exists(pluginsDir), Is.True);
        Assert.That(service.GetAll(), Is.Empty);
    }

    [Test]
    public void ScanPlugins_should_discover_and_register_valid_plugin_in_appdata_plugins_folder()
    {
        CreatePluginDirectory("my-awesome-plugin");

        using var service = new PluginService(_appFolderInfo, _supervisor);

        var plugins = service.GetAll();
        Assert.That(plugins.Count, Is.EqualTo(1));

        var plugin = plugins[0];
        Assert.That(plugin.Manifest.Id, Is.EqualTo("my-awesome-plugin"));
        Assert.That(plugin.Manifest.Name, Is.EqualTo("Plugin my-awesome-plugin"));
        Assert.That(plugin.State, Is.EqualTo(PluginState.Stopped));
        Assert.That(plugin.Enabled, Is.False);
    }

    [Test]
    public void ScanPlugins_should_discover_multiple_plugins_correctly()
    {
        CreatePluginDirectory("plugin-one");
        CreatePluginDirectory("plugin-two");
        CreatePluginDirectory("plugin-three");

        using var service = new PluginService(_appFolderInfo, _supervisor);

        var plugins = service.GetAll();
        Assert.That(plugins.Count, Is.EqualTo(3));

        var ids = plugins.Select(p => p.Manifest.Id).ToList();
        Assert.That(ids, Does.Contain("plugin-one"));
        Assert.That(ids, Does.Contain("plugin-two"));
        Assert.That(ids, Does.Contain("plugin-three"));
    }

    [Test]
    public void ScanPlugins_should_skip_directories_without_plugin_json()
    {
        var emptyPluginDir = Path.Combine(_pluginsFolder, "unmanifested-plugin");
        Directory.CreateDirectory(emptyPluginDir);
        File.WriteAllText(Path.Combine(emptyPluginDir, "somefile.txt"), "no manifest here");

        using var service = new PluginService(_appFolderInfo, _supervisor);

        Assert.That(service.GetAll(), Is.Empty);
    }

    [Test]
    public void ScanPlugins_should_skip_malformed_json_manifest_without_throwing()
    {
        var badDir = Path.Combine(_pluginsFolder, "corrupt-plugin");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "plugin.json"), "{ invalid json [} ");

        using var service = new PluginService(_appFolderInfo, _supervisor);

        Assert.That(service.GetAll(), Is.Empty);
    }

    [Test]
    public void ScanPlugins_should_skip_manifests_violating_security_validation()
    {
        var maliciousDir = Path.Combine(_pluginsFolder, "escape-plugin");
        Directory.CreateDirectory(maliciousDir);

        var manifest = new PluginManifest
        {
            Id = "escape-plugin",
            Name = "Escaper",
            Version = "1.0.0",
            Entrypoint = "../../outside.sh"
        };
        File.WriteAllText(Path.Combine(maliciousDir, "plugin.json"), JsonSerializer.Serialize(manifest, McpJsonOptions.Default));

        CreatePluginDirectory("legit-plugin");

        using var service = new PluginService(_appFolderInfo, _supervisor);

        var plugins = service.GetAll();
        Assert.That(plugins.Count, Is.EqualTo(1));
        Assert.That(plugins[0].Manifest.Id, Is.EqualTo("legit-plugin"));
    }

    [Test]
    public void ScanPlugins_should_not_overwrite_existing_plugin_with_same_id_on_rescan()
    {
        CreatePluginDirectory("persist-plugin");

        using var service = new PluginService(_appFolderInfo, _supervisor);

        var firstInstance = service.Get("persist-plugin");
        Assert.That(firstInstance, Is.Not.Null);

        // Rescan plugins
        service.ScanPlugins();

        var secondInstance = service.Get("persist-plugin");
        Assert.That(ReferenceEquals(firstInstance, secondInstance), Is.True);
    }

    [Test]
    public void Get_with_valid_and_case_insensitive_id_should_return_plugin_info()
    {
        CreatePluginDirectory("case-sensitive-id");

        using var service = new PluginService(_appFolderInfo, _supervisor);

        var matchExact = service.Get("case-sensitive-id");
        var matchUpper = service.Get("CASE-SENSITIVE-ID");

        Assert.That(matchExact, Is.Not.Null);
        Assert.That(matchUpper, Is.Not.Null);
        Assert.That(matchExact.Manifest.Id, Is.EqualTo("case-sensitive-id"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("nonexistent-plugin-id")]
    public void Get_with_invalid_or_missing_id_should_return_null(string id)
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var result = service.Get(id);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void RegisterPlugin_with_null_manifest_should_throw_ArgumentNullException()
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        Assert.Throws<ArgumentNullException>(() => service.RegisterPlugin(null, _tempAppData));
    }

    [Test]
    public void RegisterPlugin_with_custom_host_should_store_and_return_custom_host_info()
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var pluginDir = Path.Combine(_tempAppData, "custom_plugin_dir");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "entry.sh"), "#!/bin/sh");

        var manifest = new PluginManifest
        {
            Id = "custom-host-plugin",
            Name = "Custom Host Plugin",
            Version = "2.0.0",
            Entrypoint = "entry.sh"
        };

        var customHost = Substitute.For<ISidecarProcessHost>();
        var customInfo = new PluginInfo
        {
            Manifest = manifest,
            State = PluginState.Running,
            Enabled = true,
            ProcessId = 9999
        };
        customHost.PluginInfo.Returns(customInfo);

        var registered = service.RegisterPlugin(manifest, pluginDir, customHost);

        Assert.That(registered, Is.Not.Null);
        Assert.That(registered.Manifest.Id, Is.EqualTo("custom-host-plugin"));
        Assert.That(registered.ProcessId, Is.EqualTo(9999));
        Assert.That(service.Get("custom-host-plugin"), Is.EqualTo(customInfo));
    }

    [Test]
    public void Enable_with_valid_id_should_set_enabled_true_and_start_host()
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var pluginDir = Path.Combine(_tempAppData, "enable_test");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "entry.sh"), "#!/bin/sh");

        var manifest = new PluginManifest
        {
            Id = "enable-plugin",
            Name = "Enable Test",
            Version = "1.0.0",
            Entrypoint = "entry.sh"
        };

        var mockHost = Substitute.For<ISidecarProcessHost>();
        var info = new PluginInfo { Manifest = manifest, State = PluginState.Stopped, Enabled = false };
        mockHost.PluginInfo.Returns(info);

        service.RegisterPlugin(manifest, pluginDir, mockHost);

        var enabledInfo = service.Enable("enable-plugin");

        Assert.That(enabledInfo, Is.Not.Null);
        Assert.That(enabledInfo.Enabled, Is.True);
        mockHost.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("unknown-plugin")]
    public void Enable_with_invalid_or_missing_id_should_return_null(string id)
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var result = service.Enable(id);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Disable_with_valid_id_should_set_enabled_false_stop_host_and_set_state_disabled()
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var pluginDir = Path.Combine(_tempAppData, "disable_test");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "entry.sh"), "#!/bin/sh");

        var manifest = new PluginManifest
        {
            Id = "disable-plugin",
            Name = "Disable Test",
            Version = "1.0.0",
            Entrypoint = "entry.sh"
        };

        var mockHost = Substitute.For<ISidecarProcessHost>();
        var info = new PluginInfo { Manifest = manifest, State = PluginState.Running, Enabled = true };
        mockHost.PluginInfo.Returns(info);

        service.RegisterPlugin(manifest, pluginDir, mockHost);

        var disabledInfo = service.Disable("disable-plugin");

        Assert.That(disabledInfo, Is.Not.Null);
        Assert.That(disabledInfo.Enabled, Is.False);
        Assert.That(disabledInfo.State, Is.EqualTo(PluginState.Disabled));
        mockHost.Received(1).StopAsync(Arg.Any<TimeSpan?>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("unknown-plugin")]
    public void Disable_with_invalid_or_missing_id_should_return_null(string id)
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var result = service.Disable(id);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SendRequestAsync_with_unknown_or_null_id_should_return_jsonrpc_error_32602()
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var responseNull = await service.SendRequestAsync(null, "some_method");
        Assert.That(responseNull.Error, Is.Not.Null);
        Assert.That(responseNull.Error.Code, Is.EqualTo(-32602));

        var responseUnknown = await service.SendRequestAsync("unknown_plugin", "some_method");
        Assert.That(responseUnknown.Error, Is.Not.Null);
        Assert.That(responseUnknown.Error.Code, Is.EqualTo(-32602));
        Assert.That(responseUnknown.Error.Message, Does.Contain("Plugin 'unknown_plugin' not found."));
    }

    [Test]
    public async Task SendRequestAsync_with_valid_id_should_delegate_to_host()
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var pluginDir = Path.Combine(_tempAppData, "req_test");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "entry.sh"), "#!/bin/sh");

        var manifest = new PluginManifest
        {
            Id = "req-plugin",
            Name = "Req Plugin",
            Version = "1.0.0",
            Entrypoint = "entry.sh"
        };

        var mockHost = Substitute.For<ISidecarProcessHost>();
        var info = new PluginInfo { Manifest = manifest, State = PluginState.Running, Enabled = true };
        mockHost.PluginInfo.Returns(info);

        var expectedResponse = JsonRpcResponse.Success("req-1", new { status = "ok" });
        mockHost.SendRequestAsync("indexer/search", Arg.Any<object>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        service.RegisterPlugin(manifest, pluginDir, mockHost);

        var response = await service.SendRequestAsync("req-plugin", "indexer/search", new { query = "test" });

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Error, Is.Null);
        Assert.That(response.Id, Is.EqualTo("req-1"));
    }

    [Test]
    public void Handle_ApplicationShutdownRequested_should_stop_all_active_plugin_hosts()
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var pluginDir = Path.Combine(_tempAppData, "shutdown_test");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "entry.sh"), "#!/bin/sh");

        var manifest = new PluginManifest { Id = "shutdown-p1", Name = "P1", Version = "1.0", Entrypoint = "entry.sh" };
        var mockHost = Substitute.For<ISidecarProcessHost>();
        mockHost.PluginInfo.Returns(new PluginInfo { Manifest = manifest });
        mockHost.StopAsync(Arg.Any<TimeSpan?>()).Returns(Task.CompletedTask);

        service.RegisterPlugin(manifest, pluginDir, mockHost);

        service.Handle(new ApplicationShutdownRequested());

        mockHost.Received(1).StopAsync(Arg.Any<TimeSpan?>());
    }

    [Test]
    public void Handle_ApplicationShutdownRequested_when_host_throws_should_handle_gracefully()
    {
        using var service = new PluginService(_appFolderInfo, _supervisor);

        var pluginDir = Path.Combine(_tempAppData, "shutdown_err_test");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "entry.sh"), "#!/bin/sh");

        var manifest = new PluginManifest { Id = "shutdown-err", Name = "PErr", Version = "1.0", Entrypoint = "entry.sh" };
        var mockHost = Substitute.For<ISidecarProcessHost>();
        mockHost.PluginInfo.Returns(new PluginInfo { Manifest = manifest });
        mockHost.StopAsync(Arg.Any<TimeSpan?>()).Returns(Task.FromException(new InvalidOperationException("Process hung")));

        service.RegisterPlugin(manifest, pluginDir, mockHost);

        Assert.DoesNotThrow(() => service.Handle(new ApplicationShutdownRequested()));
    }

    [Test]
    public void Dispose_should_dispose_all_plugin_hosts_and_clear_registry()
    {
        var service = new PluginService(_appFolderInfo, _supervisor);

        var pluginDir = Path.Combine(_tempAppData, "dispose_test");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "entry.sh"), "#!/bin/sh");

        var manifest = new PluginManifest { Id = "dispose-p", Name = "PDisp", Version = "1.0", Entrypoint = "entry.sh" };
        var mockHost = Substitute.For<ISidecarProcessHost>();
        mockHost.PluginInfo.Returns(new PluginInfo { Manifest = manifest });

        service.RegisterPlugin(manifest, pluginDir, mockHost);

        service.Dispose();

        mockHost.Received(1).Dispose();
        Assert.That(service.GetAll(), Is.Empty);
    }

    [Test]
    public void SidecarProcessHost_constructor_should_initialize_stopped_state()
    {
        var manifest = new PluginManifest { Id = "host-init", Name = "Init", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);

        Assert.That(host.PluginInfo, Is.Not.Null);
        Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Stopped));
        Assert.That(host.PluginInfo.Enabled, Is.False);
        Assert.That(host.PluginInfo.PluginDirectory, Is.EqualTo(_tempAppData));
        Assert.That(host.IsRunning, Is.False);
    }

    [Test]
    public void SidecarProcessHost_NotificationReceived_event_should_be_raised_on_jsonrpc_notifications()
    {
        var manifest = new PluginManifest { Id = "notif-test", Name = "Notif", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);

        string receivedMethod = null;
        string receivedMessage = null;
        host.NotificationReceived += (sender, args) =>
        {
            receivedMethod = args.Method;
            receivedMessage = args.RawMessage;
        };

        var notificationJson = "{\"jsonrpc\":\"2.0\",\"method\":\"torrent/completed\",\"params\":{\"id\":42}}";
        host.ProcessStdoutLine(notificationJson);

        Assert.That(receivedMethod, Is.EqualTo("torrent/completed"));
        Assert.That(receivedMessage, Is.EqualTo(notificationJson));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void SidecarProcessHost_ProcessStdoutLine_should_ignore_empty_or_whitespace_lines(string line)
    {
        var manifest = new PluginManifest { Id = "empty-line-test", Name = "Empty", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);

        var eventFired = false;
        host.NotificationReceived += (sender, args) => { eventFired = true; };

        Assert.DoesNotThrow(() => host.ProcessStdoutLine(line));
        Assert.That(eventFired, Is.False);
    }

    [Test]
    public void SidecarProcessHost_ProcessStdoutLine_should_ignore_unparseable_json()
    {
        var manifest = new PluginManifest { Id = "bad-json-test", Name = "Bad", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);

        var eventFired = false;
        host.NotificationReceived += (sender, args) => { eventFired = true; };

        Assert.DoesNotThrow(() => host.ProcessStdoutLine("this is just plain text output from a script"));
        Assert.That(eventFired, Is.False);
    }

    [Test]
    public void SidecarProcessHost_CalculateBackoff_should_calculate_exponential_delays()
    {
        var manifest = new PluginManifest { Id = "backoff-test", Name = "Backoff", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);

        Assert.That(host.CalculateBackoff(0), Is.EqualTo(TimeSpan.Zero));
        Assert.That(host.CalculateBackoff(1), Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(host.CalculateBackoff(2), Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(host.CalculateBackoff(3), Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(host.CalculateBackoff(4), Is.EqualTo(TimeSpan.FromSeconds(8)));
        Assert.That(host.CalculateBackoff(5), Is.EqualTo(TimeSpan.FromSeconds(16)));
        Assert.That(host.CalculateBackoff(6), Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(host.CalculateBackoff(10), Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void SidecarProcessHost_RecordCrash_exceeding_threshold_should_disable_plugin_and_set_errored_state()
    {
        var manifest = new PluginManifest { Id = "crash-threshold", Name = "Crash", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);
        host.PluginInfo.Enabled = true;

        var now = DateTime.UtcNow;

        for (var i = 1; i <= 4; i++)
        {
            var backoff = host.RecordCrash(now.AddSeconds(i * 2));
            Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(i));
            Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Crashed));
            Assert.That(backoff, Is.GreaterThan(TimeSpan.Zero));
        }

        // 5th crash within 60s
        var finalBackoff = host.RecordCrash(now.AddSeconds(10));
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(5));
        Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Errored));
        Assert.That(host.PluginInfo.Enabled, Is.False);
        Assert.That(finalBackoff, Is.EqualTo(TimeSpan.Zero));
        Assert.That(host.PluginInfo.LastError, Does.Contain("exceeded maximum crash threshold"));
    }

    [Test]
    public void SidecarProcessHost_RecordCrash_should_purge_crashes_older_than_60_seconds()
    {
        var manifest = new PluginManifest { Id = "purge-crash", Name = "Purge", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);
        host.PluginInfo.Enabled = true;

        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        host.RecordCrash(t0);
        host.RecordCrash(t0.AddSeconds(15));
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(2));

        // 70 seconds after t0: the first crash expires and is pruned
        host.RecordCrash(t0.AddSeconds(70));
        Assert.That(host.PluginInfo.CrashCount, Is.EqualTo(2)); // t0 pruned, leaving +15 and +70
        Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Crashed));
    }

    [Test]
    public void SidecarProcessHost_Kill_should_transition_state_to_Stopped()
    {
        var manifest = new PluginManifest { Id = "kill-test", Name = "Kill", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);

        host.Kill();

        Assert.That(host.PluginInfo.State, Is.EqualTo(PluginState.Stopped));
        Assert.That(host.PluginInfo.ProcessId, Is.Null);
    }

    [Test]
    public void SidecarProcessHost_SendRequestAsync_when_not_running_should_throw_InvalidOperationException()
    {
        var manifest = new PluginManifest { Id = "req-not-running", Name = "Req", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await host.SendRequestAsync("ping");
        });
    }

    [Test]
    public async Task SidecarProcessHost_SendNotificationAsync_when_not_running_should_return_cleanly_without_throwing()
    {
        var manifest = new PluginManifest { Id = "notif-not-running", Name = "Notif", Version = "1.0", Entrypoint = "p.sh" };
        using var host = new SidecarProcessHost(manifest, _tempAppData);

        await host.SendNotificationAsync("ping_notification");
        Assert.Pass();
    }

    [Test]
    public void SidecarProcessHost_Dispose_should_clean_up_resources_safely()
    {
        var manifest = new PluginManifest { Id = "dispose-test", Name = "Disp", Version = "1.0", Entrypoint = "p.sh" };
        var host = new SidecarProcessHost(manifest, _tempAppData);

        Assert.DoesNotThrow(() => host.Dispose());
        // Second dispose should be safe and idempotent
        Assert.DoesNotThrow(() => host.Dispose());
    }
}
