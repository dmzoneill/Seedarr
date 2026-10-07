using System;
using System.Net;
using System.Net.Http;

namespace NzbDrone.Core.Test;

internal static class TestSocketsHttpHandlers
{
    /// <summary>
    /// Proxy handler that fails quickly instead of waiting on real network I/O.
    /// </summary>
    public static SocketsHttpHandler FastFailingProxyHandler()
    {
        return new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(50),
            Proxy = new WebProxy("http://127.0.0.1:9"),
            UseProxy = true
        };
    }

    /// <summary>
    /// Direct handler with a short connect timeout for tests that should not reach the network.
    /// </summary>
    public static SocketsHttpHandler FastFailingHandler()
    {
        return new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(50)
        };
    }
}
