using System.Net.Http;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Indexers;

internal sealed class IndexerProxyState
{
    public HttpClient Client { get; private set; }

    public SocketsHttpHandler Handler { get; private set; }

    public bool LastEnabled { get; private set; }

    public string LastHost { get; private set; }

    public int LastPort { get; private set; }

    public ProxyType LastType { get; private set; }

    public void Ensure(IProxySettingsProvider proxy, object syncLock, HttpClient fallback)
    {
        if (proxy == null || !proxy.IsEnabled)
        {
            lock (syncLock)
            {
                LastEnabled = false;
                Client = null;
                Handler = null;
            }

            return;
        }

        lock (syncLock)
        {
            var host = proxy.Host;
            var port = proxy.Port;
            var type = proxy.Type;
            if (Client != null && LastEnabled && LastHost == host && LastPort == port && LastType == type)
            {
                return;
            }

            Handler = proxy.CreateHandler();
            Client = Handler != null
                ? new HttpClient(Handler) { Timeout = fallback.Timeout }
                : fallback;
            LastEnabled = true;
            LastHost = host;
            LastPort = port;
            LastType = type;
        }
    }
}
