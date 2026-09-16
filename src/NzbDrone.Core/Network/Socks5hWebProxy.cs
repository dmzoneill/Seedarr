using System;
using System.Net;

namespace NzbDrone.Core.Network;

public class Socks5hWebProxy : WebProxy, IWebProxy
{
    private readonly Uri _transportUri;

    public Socks5hWebProxy(string uri)
        : base(uri)
    {
        var builder = new UriBuilder(uri);
        if (builder.Scheme.Equals("socks5h", StringComparison.OrdinalIgnoreCase))
        {
            builder.Scheme = "socks5";
        }

        _transportUri = builder.Uri;
    }

    public Socks5hWebProxy(Uri address)
        : base(address)
    {
        var builder = new UriBuilder(address);
        if (builder.Scheme.Equals("socks5h", StringComparison.OrdinalIgnoreCase))
        {
            builder.Scheme = "socks5";
        }

        _transportUri = builder.Uri;
    }

    public new Uri GetProxy(Uri destination) => _transportUri;

    Uri IWebProxy.GetProxy(Uri destination) => _transportUri;
}
