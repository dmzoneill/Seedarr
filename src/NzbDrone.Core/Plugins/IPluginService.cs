using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Mcp;

namespace NzbDrone.Core.Plugins;

public interface IPluginService
{
    IReadOnlyList<PluginInfo> GetAll();
    PluginInfo Get(string id);
    PluginInfo Enable(string id);
    PluginInfo Disable(string id);
    void ScanPlugins();
    Task<JsonRpcResponse> SendRequestAsync(string id, string method, object @params = null, TimeSpan? timeout = null);
}
