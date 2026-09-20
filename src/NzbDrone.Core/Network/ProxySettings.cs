using System;
using System.Net;
using System.Net.Http;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Network;

public enum ProxyType
{
    None,
    Http,
    Socks5
}

public interface IProxySettingsProvider
{
    ProxyType Type { get; }
    string Host { get; }
    int Port { get; }
    string Username { get; }
    string Password { get; }
    bool IsEnabled { get; }
    SocketsHttpHandler CreateHandler();
}

public class ProxySettingsProvider : IProxySettingsProvider
{
    private readonly IConfigService _configService;
    private readonly Logger _logger;

    public ProxySettingsProvider(IConfigService configService)
    {
        _configService = configService;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public ProxyType Type => Enum.TryParse<ProxyType>(_configService.GetValue("ProxyType", "None"), ignoreCase: true, out var type) ? type : ProxyType.None;
    public string Host => _configService.GetValue("ProxyHost", "");
    public int Port => _configService.GetValueInt("ProxyPort", 8080);
    public string Username => _configService.GetValue("ProxyUsername", "");
    public string Password => _configService.GetValue("ProxyPassword", "");
    public bool IsEnabled => Type != ProxyType.None && !string.IsNullOrEmpty(Host);

    public SocketsHttpHandler CreateHandler()
    {
        if (!IsEnabled)
        {
            return new SocketsHttpHandler();
        }

        var proxyUri = Type switch
        {
            ProxyType.Http => $"http://{Host}:{Port}",
            ProxyType.Socks5 => $"socks5h://{Host}:{Port}",
            _ => null
        };

        if (proxyUri == null)
        {
            return new SocketsHttpHandler();
        }

        _logger.Debug("Using proxy: {0}", proxyUri);

        var proxy = Type == ProxyType.Socks5
            ? (WebProxy)new Socks5hWebProxy(proxyUri)
            : new WebProxy(proxyUri);

        if (_configService.ProxyAuthEnabled)
        {
            proxy.Credentials = new NetworkCredential(Username, Password);
        }

        return new SocketsHttpHandler
        {
            Proxy = proxy,
            UseProxy = true
        };
    }
}
