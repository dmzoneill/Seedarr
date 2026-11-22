using System.Net.Http;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Indexers;

internal static class IndexerProxyClient
{
    public static void Ensure(
        IProxySettingsProvider proxy,
        object syncLock,
        ref HttpClient proxyClient,
        ref SocketsHttpHandler proxyHandler,
        ref bool lastEnabled,
        ref string lastHost,
        ref int lastPort,
        ref ProxyType lastType,
        HttpClient fallback)
    {
        if (proxy == null || !proxy.IsEnabled)
        {
            lock (syncLock)
            {
                lastEnabled = false;
                proxyClient = null;
                proxyHandler = null;
            }

            return;
        }

        lock (syncLock)
        {
            var host = proxy.Host;
            var port = proxy.Port;
            var type = proxy.Type;
            if (proxyClient != null && lastEnabled && lastHost == host && lastPort == port && lastType == type)
            {
                return;
            }

            proxyHandler = proxy.CreateHandler();
            proxyClient = proxyHandler != null
                ? new HttpClient(proxyHandler) { Timeout = fallback.Timeout }
                : fallback;
            lastEnabled = true;
            lastHost = host;
            lastPort = port;
            lastType = type;
        }
    }
}
