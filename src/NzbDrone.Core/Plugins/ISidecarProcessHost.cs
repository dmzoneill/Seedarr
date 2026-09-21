using System;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Mcp;

namespace NzbDrone.Core.Plugins;

public interface ISidecarProcessHost : IDisposable
{
    PluginInfo PluginInfo { get; }
    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(TimeSpan? timeout = null);
    void Kill();

    Task<JsonRpcResponse> SendRequestAsync(string method, object @params = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    Task SendNotificationAsync(string method, object @params = null, CancellationToken cancellationToken = default);
}
