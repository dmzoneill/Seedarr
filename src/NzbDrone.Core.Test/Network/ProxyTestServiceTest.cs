using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;
using NzbDrone.Core.Peers;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class ProxyTestServiceTest
{
    private IConfigService _configService;
    private ProxyTestService _subject;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _subject = new ProxyTestService(_configService);
    }

    [Test]
    public async Task TestProxyAsync_should_return_success_on_reachable_socks5_proxy_and_verify_remote_dns()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();

            // Greeting: 0x05 0x02 0x00 0x02 (auth supported)
            var greeting = new byte[4];
            PeerConnection.ReadExactBytes(stream, greeting, 0, 4);
            Assert.That(greeting[0], Is.EqualTo(0x05));
            // Choose auth method 0x02 (username/password)
            stream.Write(new byte[] { 0x05, 0x02 });
            stream.Flush();

            // Auth subnegotiation: 0x01 [ulen] [user] [plen] [pass]
            var authVer = stream.ReadByte();
            Assert.That(authVer, Is.EqualTo(0x01));
            var ulen = stream.ReadByte();
            var userBytes = new byte[ulen];
            PeerConnection.ReadExactBytes(stream, userBytes, 0, ulen);
            var username = Encoding.UTF8.GetString(userBytes);

            var plen = stream.ReadByte();
            var passBytes = new byte[plen];
            PeerConnection.ReadExactBytes(stream, passBytes, 0, plen);
            var password = Encoding.UTF8.GetString(passBytes);

            Assert.That(username, Is.EqualTo("testuser"));
            Assert.That(password, Is.EqualTo("secret123"));

            // Auth success
            stream.Write(new byte[] { 0x01, 0x00 });
            stream.Flush();

            // Connect command: 0x05 0x01 0x00 0x03 (domain name remote DNS!)
            var connectHdr = new byte[4];
            PeerConnection.ReadExactBytes(stream, connectHdr, 0, 4);
            Assert.That(connectHdr[0], Is.EqualTo(0x05));
            Assert.That(connectHdr[1], Is.EqualTo(0x01));
            Assert.That(connectHdr[3], Is.EqualTo(0x03)); // ATYP: domain name (verifies remote DNS, no leak!)

            var dlen = stream.ReadByte();
            var domainBytes = new byte[dlen + 2]; // domain + 2 port bytes
            PeerConnection.ReadExactBytes(stream, domainBytes, 0, domainBytes.Length);

            // Connect success reply: 0x05 0x00 0x00 0x01 [4 ip bytes] [2 port bytes]
            stream.Write(new byte[] { 0x05, 0x00, 0x00, 0x01, 127, 0, 0, 1, 0x00, 0x50 });
            stream.Flush();
        });

        var request = new ProxyTestRequest
        {
            ProxyType = "socks5h",
            ProxyHost = "127.0.0.1",
            ProxyPort = port,
            ProxyAuthEnabled = true,
            ProxyUsername = "testuser",
            ProxyPassword = "secret123",
            TestTargetHost = "tracker.example.com",
            TestTargetPort = 80,
            TimeoutMs = 3000
        };

        try
        {
            var result = await _subject.TestProxyAsync(request);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.True);
            Assert.That(result.RemoteDnsVerified, Is.True);
            Assert.That(result.Message, Does.Contain("verified successfully"));
            Assert.That(serverTask.Wait(TimeSpan.FromSeconds(3)), Is.True);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task TestProxyAsync_should_return_failure_on_bad_credentials()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();

            var greeting = new byte[4];
            PeerConnection.ReadExactBytes(stream, greeting, 0, 4);
            stream.Write(new byte[] { 0x05, 0x02 }); // Auth requested
            stream.Flush();

            var authVer = stream.ReadByte();
            var ulen = stream.ReadByte();
            var userBytes = new byte[ulen];
            PeerConnection.ReadExactBytes(stream, userBytes, 0, ulen);

            var plen = stream.ReadByte();
            var passBytes = new byte[plen];
            PeerConnection.ReadExactBytes(stream, passBytes, 0, plen);

            // Auth failed status 0x01
            stream.Write(new byte[] { 0x01, 0x01 });
            stream.Flush();
        });

        var request = new ProxyTestRequest
        {
            ProxyType = "socks5",
            ProxyHost = "127.0.0.1",
            ProxyPort = port,
            ProxyAuthEnabled = true,
            ProxyUsername = "testuser",
            ProxyPassword = "wrongpassword",
            TimeoutMs = 3000
        };

        try
        {
            var result = await _subject.TestProxyAsync(request);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("authentication failed"));
            Assert.That(serverTask.Wait(TimeSpan.FromSeconds(3)), Is.True);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task TestProxyAsync_should_return_failure_on_unreachable_proxy()
    {
        var request = new ProxyTestRequest
        {
            ProxyType = "socks5",
            ProxyHost = "192.0.2.1", // TEST-NET-1 unroutable/packet drop
            ProxyPort = 1080,
            TimeoutMs = 150
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _subject.TestProxyAsync(request);
        stopwatch.Stop();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("timed out").Or.Contain("Failed to connect"));
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(3000));
    }

    [Test]
    public async Task TestProxyAsync_should_return_success_on_reachable_http_proxy()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();

            var buffer = new byte[1024];
            var read = stream.Read(buffer, 0, buffer.Length);
            var req = Encoding.ASCII.GetString(buffer, 0, read);

            Assert.That(req, Does.StartWith("CONNECT example.com:80 HTTP/1.1"));

            var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            stream.Write(response, 0, response.Length);
            stream.Flush();
        });

        var request = new ProxyTestRequest
        {
            ProxyType = "http",
            ProxyHost = "127.0.0.1",
            ProxyPort = port,
            TestTargetHost = "example.com",
            TestTargetPort = 80,
            TimeoutMs = 3000
        };

        try
        {
            var result = await _subject.TestProxyAsync(request);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.True);
            Assert.That(result.RemoteDnsVerified, Is.True);
            Assert.That(serverTask.Wait(TimeSpan.FromSeconds(3)), Is.True);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task TestProxyAsync_should_return_failure_on_http_proxy_407_auth_required()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();

            var buffer = new byte[1024];
            var read = stream.Read(buffer, 0, buffer.Length);

            var response = Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n");
            stream.Write(response, 0, response.Length);
            stream.Flush();
        });

        var request = new ProxyTestRequest
        {
            ProxyType = "http",
            ProxyHost = "127.0.0.1",
            ProxyPort = port,
            TimeoutMs = 3000
        };

        try
        {
            var result = await _subject.TestProxyAsync(request);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("407"));
            Assert.That(serverTask.Wait(TimeSpan.FromSeconds(3)), Is.True);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task TestProxyAsync_should_fail_when_host_is_empty()
    {
        var request = new ProxyTestRequest
        {
            ProxyType = "socks5",
            ProxyHost = "",
            ProxyPort = 1080
        };

        var result = await _subject.TestProxyAsync(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("hostname or IP address must be specified"));
    }

    [Test]
    public async Task TestProxyAsync_should_fail_when_type_is_invalid()
    {
        var request = new ProxyTestRequest
        {
            ProxyType = "unknown",
            ProxyHost = "127.0.0.1",
            ProxyPort = 1080
        };

        var result = await _subject.TestProxyAsync(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("valid proxy protocol"));
    }
}
