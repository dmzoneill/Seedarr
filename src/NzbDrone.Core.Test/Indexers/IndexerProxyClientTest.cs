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
        var proxy = Substitute.For<IProxySettingsProvider>();
        proxy.IsEnabled.Returns(false);
        HttpClient client = new();
        SocketsHttpHandler handler = new();
        var enabled = true;
        var host = "old";
        var port = 1;
        var type = ProxyType.Http;
        using var fallback = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        IndexerProxyClient.Ensure(proxy, new object(), ref client, ref handler, ref enabled, ref host, ref port, ref type, fallback);

        Assert.That(client, Is.Null);
        Assert.That(handler, Is.Null);
        Assert.That(enabled, Is.False);
    }

    [Test]
    public void Ensure_uses_the_fallback_timeout_for_a_proxy_handler()
    {
        var proxy = Substitute.For<IProxySettingsProvider>();
        proxy.IsEnabled.Returns(true);
        proxy.Host.Returns("127.0.0.1");
        proxy.Port.Returns(8080);
        proxy.Type.Returns(ProxyType.Http);
        proxy.CreateHandler().Returns(new SocketsHttpHandler());
        HttpClient client = null;
        SocketsHttpHandler handler = null;
        var enabled = false;
        var host = "";
        var port = 0;
        var type = ProxyType.None;
        using var fallback = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        IndexerProxyClient.Ensure(proxy, new object(), ref client, ref handler, ref enabled, ref host, ref port, ref type, fallback);

        Assert.That(client, Is.Not.Null);
        Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(enabled, Is.True);
        Assert.That(host, Is.EqualTo("127.0.0.1"));

        var same = client;
        IndexerProxyClient.Ensure(proxy, new object(), ref client, ref handler, ref enabled, ref host, ref port, ref type, fallback);
        Assert.That(client, Is.SameAs(same));

        proxy.Host.Returns("10.0.0.2");
        IndexerProxyClient.Ensure(proxy, new object(), ref client, ref handler, ref enabled, ref host, ref port, ref type, fallback);
        Assert.That(client, Is.Not.SameAs(same));
        Assert.That(host, Is.EqualTo("10.0.0.2"));
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
        HttpClient client = null;
        SocketsHttpHandler handler = null;
        var enabled = false;
        var host = "";
        var port = 0;
        var type = ProxyType.None;
        using var fallback = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        IndexerProxyClient.Ensure(null, new object(), ref client, ref handler, ref enabled, ref host, ref port, ref type, fallback);
        Assert.That(enabled, Is.False);

        IndexerProxyClient.Ensure(proxy, new object(), ref client, ref handler, ref enabled, ref host, ref port, ref type, fallback);
        Assert.That(client, Is.SameAs(fallback));
        Assert.That(enabled, Is.True);
        Assert.That(type, Is.EqualTo(ProxyType.Socks5));
    }
}
