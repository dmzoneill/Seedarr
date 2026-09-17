using System;
using System.Net;
using System.Net.Http;

namespace NzbDrone.Core.WebSeeds;

public interface IWebSeedHttpClientFactory
{
    HttpClient GetClient();
    HttpClient CreateClient();
    SocketsHttpHandler CreateHandler();
}

public class WebSeedHttpClientFactory : IWebSeedHttpClientFactory, IDisposable
{
    private readonly Lazy<HttpClient> _client;
    private readonly Lazy<SocketsHttpHandler> _handler;

    public WebSeedHttpClientFactory()
    {
        _handler = new Lazy<SocketsHttpHandler>(CreateHandler);
        _client = new Lazy<HttpClient>(() => CreateClient(_handler.Value));
    }

    public virtual SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 4,
            EnableMultipleHttp2Connections = true
        };
    }

    public virtual HttpClient CreateClient()
    {
        return CreateClient(CreateHandler());
    }

    public virtual HttpClient CreateClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler, disposeHandler: false)
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }

    public HttpClient GetClient()
    {
        return _client.Value;
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }

        if (_handler.IsValueCreated)
        {
            _handler.Value.Dispose();
        }
    }
}
