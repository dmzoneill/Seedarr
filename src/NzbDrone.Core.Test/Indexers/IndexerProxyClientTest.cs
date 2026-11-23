using System;
using System.Net.Http;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Network;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class IndexerProxyClientTest
{
    [Test]
    public void Ensure_clears_the_client_when_proxy_is_disabled()
    {
        var proxy = EnabledProxy("127.0.0.1", 8080, ProxyType.Http, new SocketsHttpHandler());
        var state = new IndexerProxyState();
        var syncLock = new object();
        using var fallback = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        state.Ensure(proxy, syncLock, fallback);
        Assert.That(state.Client, Is.Not.Null);
        Assert.That(state.Handler, Is.Not.Null);
        Assert.That(state.LastEnabled, Is.True);

        proxy.IsEnabled.Returns(false);
        state.Ensure(proxy, syncLock, fallback);

        Assert.That(state.Client, Is.Null);
        Assert.That(state.Handler, Is.Null);
        Assert.That(state.LastEnabled, Is.False);
        proxy.Received(1).CreateHandler();

        state.Ensure(null, syncLock, fallback);
        Assert.That(state.Client, Is.Null);
        Assert.That(state.Handler, Is.Null);
        Assert.That(state.LastEnabled, Is.False);
    }

    [Test]
    public void Ensure_uses_the_fallback_timeout_for_a_proxy_handler()
    {
        var handler = new SocketsHttpHandler();
        var proxy = EnabledProxy("127.0.0.1", 8080, ProxyType.Http, handler);
        var state = new IndexerProxyState();
        using var fallback = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

        state.Ensure(proxy, new object(), fallback);

        Assert.That(state.Client, Is.Not.Null);
        Assert.That(state.Client, Is.Not.SameAs(fallback));
        Assert.That(state.Client.Timeout, Is.EqualTo(fallback.Timeout));
        Assert.That(state.Handler, Is.SameAs(handler));
        Assert.That(state.LastEnabled, Is.True);
        Assert.That(state.LastHost, Is.EqualTo("127.0.0.1"));
        Assert.That(state.LastPort, Is.EqualTo(8080));
        Assert.That(state.LastType, Is.EqualTo(ProxyType.Http));
    }

    [Test]
    public void Ensure_reuses_the_client_when_settings_are_unchanged()
    {
        var proxy = EnabledProxy("127.0.0.1", 8080, ProxyType.Http, new SocketsHttpHandler());
        var state = new IndexerProxyState();
        var syncLock = new object();
        using var fallback = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        state.Ensure(proxy, syncLock, fallback);
        var client = state.Client;
        var handler = state.Handler;

        state.Ensure(proxy, syncLock, fallback);

        Assert.That(state.Client, Is.SameAs(client));
        Assert.That(state.Handler, Is.SameAs(handler));
        proxy.Received(1).CreateHandler();
    }

    [Test]
    public void Ensure_rebuilds_the_client_when_host_port_or_type_changes()
    {
        var proxy = Substitute.For<IProxySettingsProvider>();
        proxy.IsEnabled.Returns(true);
        proxy.Host.Returns("127.0.0.1");
        proxy.Port.Returns(8080);
        proxy.Type.Returns(ProxyType.Http);
        proxy.CreateHandler().Returns(new SocketsHttpHandler());
        var state = new IndexerProxyState();
        var syncLock = new object();
        using var fallback = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };

        state.Ensure(proxy, syncLock, fallback);
        var firstClient = state.Client;

        proxy.Host.Returns("10.0.0.2");
        var hostHandler = new SocketsHttpHandler();
        proxy.CreateHandler().Returns(hostHandler);
        state.Ensure(proxy, syncLock, fallback);

        Assert.That(state.Client, Is.Not.SameAs(firstClient));
        Assert.That(state.Client.Timeout, Is.EqualTo(fallback.Timeout));
        Assert.That(state.Handler, Is.SameAs(hostHandler));
        Assert.That(state.LastHost, Is.EqualTo("10.0.0.2"));
        Assert.That(state.LastPort, Is.EqualTo(8080));
        Assert.That(state.LastType, Is.EqualTo(ProxyType.Http));

        var afterHost = state.Client;
        proxy.Port.Returns(9090);
        var portHandler = new SocketsHttpHandler();
        proxy.CreateHandler().Returns(portHandler);
        state.Ensure(proxy, syncLock, fallback);

        Assert.That(state.Client, Is.Not.SameAs(afterHost));
        Assert.That(state.Handler, Is.SameAs(portHandler));
        Assert.That(state.LastPort, Is.EqualTo(9090));
        Assert.That(state.LastHost, Is.EqualTo("10.0.0.2"));

        var afterPort = state.Client;
        proxy.Type.Returns(ProxyType.Socks5);
        var typeHandler = new SocketsHttpHandler();
        proxy.CreateHandler().Returns(typeHandler);
        state.Ensure(proxy, syncLock, fallback);

        Assert.That(state.Client, Is.Not.SameAs(afterPort));
        Assert.That(state.Client.Timeout, Is.EqualTo(fallback.Timeout));
        Assert.That(state.Handler, Is.SameAs(typeHandler));
        Assert.That(state.LastType, Is.EqualTo(ProxyType.Socks5));
        proxy.Received(4).CreateHandler();
    }

    [Test]
    public void Ensure_keeps_the_fallback_client_when_the_handler_is_null()
    {
        var proxy = Substitute.For<IProxySettingsProvider>();
        proxy.IsEnabled.Returns(true);
        proxy.Host.Returns("127.0.0.1");
        proxy.Port.Returns(8080);
        proxy.Type.Returns(ProxyType.Socks5);
        proxy.CreateHandler().Returns((SocketsHttpHandler)null);
        var state = new IndexerProxyState();
        var syncLock = new object();
        using var fallback = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        state.Ensure(null, syncLock, fallback);
        Assert.That(state.LastEnabled, Is.False);
        Assert.That(state.Client, Is.Null);
        Assert.That(state.Handler, Is.Null);

        state.Ensure(proxy, syncLock, fallback);

        Assert.That(state.Client, Is.SameAs(fallback));
        Assert.That(state.Handler, Is.Null);
        Assert.That(state.LastEnabled, Is.True);
        Assert.That(state.LastHost, Is.EqualTo("127.0.0.1"));
        Assert.That(state.LastPort, Is.EqualTo(8080));
        Assert.That(state.LastType, Is.EqualTo(ProxyType.Socks5));

        state.Ensure(proxy, syncLock, fallback);
        Assert.That(state.Client, Is.SameAs(fallback));
        proxy.Received(1).CreateHandler();
    }

    private static IProxySettingsProvider EnabledProxy(
        string host,
        int port,
        ProxyType type,
        SocketsHttpHandler handler)
    {
        var proxy = Substitute.For<IProxySettingsProvider>();
        proxy.IsEnabled.Returns(true);
        proxy.Host.Returns(host);
        proxy.Port.Returns(port);
        proxy.Type.Returns(type);
        proxy.CreateHandler().Returns(handler);
        return proxy;
    }
}
