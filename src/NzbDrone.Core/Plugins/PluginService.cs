using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Mcp;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Processes;

namespace NzbDrone.Core.Plugins;

public class PluginService : IPluginService, IHandle<ApplicationShutdownRequested>, IDisposable
{
    private readonly IAppFolderInfo _appFolderInfo;
    private readonly ISidecarProcessSupervisor _processSupervisor;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<string, ISidecarProcessHost> _plugins = new(StringComparer.OrdinalIgnoreCase);

    public PluginService(
        IAppFolderInfo appFolderInfo = null,
        ISidecarProcessSupervisor processSupervisor = null)
    {
        _appFolderInfo = appFolderInfo;
        _processSupervisor = processSupervisor;

        ScanPlugins();
    }

    public IReadOnlyList<PluginInfo> GetAll()
    {
        return _plugins.Values.Select(h => h.PluginInfo).ToList();
    }

    public PluginInfo Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _plugins.TryGetValue(id, out var host) ? host.PluginInfo : null;
    }

    public PluginInfo Enable(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (_plugins.TryGetValue(id, out var host))
        {
            host.PluginInfo.Enabled = true;
            _ = host.StartAsync();
            return host.PluginInfo;
        }

        return null;
    }

    public PluginInfo Disable(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (_plugins.TryGetValue(id, out var host))
        {
            host.PluginInfo.Enabled = false;
            _ = host.StopAsync();
            host.PluginInfo.State = PluginState.Disabled;
            return host.PluginInfo;
        }

        return null;
    }

    public void ScanPlugins()
    {
        if (_appFolderInfo == null || string.IsNullOrWhiteSpace(_appFolderInfo.AppDataFolder))
        {
            return;
        }

        var pluginsDir = Path.Combine(_appFolderInfo.AppDataFolder, "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            try
            {
                Directory.CreateDirectory(pluginsDir);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not create plugins directory at {0}", pluginsDir);
            }

            return;
        }

        string[] pluginDirs;
        try
        {
            pluginDirs = Directory.GetDirectories(pluginsDir);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to enumerate plugins in {0}", pluginsDir);
            return;
        }

        foreach (var dir in pluginDirs)
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.Contains("..") || dirName.Contains('/') || dirName.Contains('\\'))
            {
                _logger.Warn("Ignoring suspicious plugin directory name: {0}", dirName);
                continue;
            }

            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifest>(json, McpJsonOptions.Default);
                if (manifest == null)
                {
                    _logger.Warn("Manifest at {0} deserialized to null", manifestPath);
                    continue;
                }

                manifest.ValidateAndResolveEntrypoint(dir);

                if (!_plugins.ContainsKey(manifest.Id))
                {
                    var host = new SidecarProcessHost(manifest, dir, _processSupervisor);
                    _plugins[manifest.Id] = host;
                    _logger.Info("Discovered plugin '{0}' ({1}) v{2}", manifest.Name, manifest.Id, manifest.Version);
                }
            }
            catch (SecurityException secEx)
            {
                _logger.Error(secEx, "Security violation loading plugin from {0}: {1}", dir, secEx.Message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load plugin manifest at {0}: {1}", manifestPath, ex.Message);
            }
        }
    }

    public PluginInfo RegisterPlugin(PluginManifest manifest, string pluginDirectory, ISidecarProcessHost customHost = null)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        manifest.ValidateAndResolveEntrypoint(pluginDirectory);

        var host = customHost ?? new SidecarProcessHost(manifest, pluginDirectory, _processSupervisor);
        _plugins[manifest.Id] = host;
        return host.PluginInfo;
    }

    public async Task<JsonRpcResponse> SendRequestAsync(
        string id,
        string method,
        object @params = null,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(id) || !_plugins.TryGetValue(id, out var host))
        {
            return JsonRpcResponse.CreateError(null, -32602, $"Plugin '{id}' not found.");
        }

        return await host.SendRequestAsync(method, @params, timeout);
    }

    public void Handle(ApplicationShutdownRequested message)
    {
        _logger.Info("Terminating all active plugin sidecar processes on application shutdown...");
        foreach (var host in _plugins.Values)
        {
            try
            {
                host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error stopping plugin '{0}' during shutdown", host.PluginInfo?.Manifest?.Id);
            }
        }
    }

    public void Dispose()
    {
        foreach (var host in _plugins.Values)
        {
            try
            {
                host.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to dispose plugin host");
            }
        }

        _plugins.Clear();
    }
}
