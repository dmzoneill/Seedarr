using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Network;

[TestFixture]
public class ExternalIpServiceTest
{
    // Helper: set private _cachedIp and _lastFetch fields directly on a subject instance.
    private static void SetCache(ExternalIpService subject, string ip, DateTime lastFetch)
    {
        var ipField = typeof(ExternalIpService).GetField("_cachedIp", BindingFlags.NonPublic | BindingFlags.Instance);
        var fetchField = typeof(ExternalIpService).GetField("_lastFetch", BindingFlags.NonPublic | BindingFlags.Instance);
        ipField.SetValue(subject, ip);
        fetchField.SetValue(subject, lastFetch);
    }

    private static DateTime GetLastFetch(ExternalIpService subject)
    {
        var fetchField = typeof(ExternalIpService).GetField("_lastFetch", BindingFlags.NonPublic | BindingFlags.Instance);
        return (DateTime)fetchField.GetValue(subject);
    }

    [Test]
    public void CachedIp_should_be_empty_by_default()
    {
        var subject = new ExternalIpService();

        Assert.That(subject.CachedIp, Is.EqualTo(""));
    }

    [Test]
    public async Task GetExternalIpAsync_should_return_cached_ip_when_cache_is_valid()
    {
        var subject = new ExternalIpService();
        SetCache(subject, "203.0.113.5", DateTime.UtcNow.AddMinutes(-5));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("203.0.113.5"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_not_call_network_when_cache_is_valid()
    {
        // Any network call would throw, proving the cache short-circuits.
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("must not be called"));
        var subject = new ExternalIpService(new HttpClient(handler));
        SetCache(subject, "10.0.0.1", DateTime.UtcNow.AddMinutes(-3));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("10.0.0.1"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_fetch_ip_from_first_source()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "1.2.3.4");
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("1.2.3.4"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_trim_whitespace_from_response()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "  8.8.8.8\n");
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("8.8.8.8"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_skip_invalid_ip_and_use_next_source()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "not-an-ip-address");
        handler.Enqueue(HttpStatusCode.OK, "5.6.7.8");
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("5.6.7.8"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_skip_html_response_and_try_next_source()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "<html><body>Error</body></html>");
        handler.Enqueue(HttpStatusCode.OK, "9.8.7.6");
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("9.8.7.6"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_update_cached_ip_after_successful_fetch()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "9.10.11.12");
        var subject = new ExternalIpService(new HttpClient(handler));

        await subject.GetExternalIpAsync();

        Assert.That(subject.CachedIp, Is.EqualTo("9.10.11.12"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_update_last_fetch_time_after_successful_fetch()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "9.10.11.12");
        var subject = new ExternalIpService(new HttpClient(handler));

        var before = DateTime.UtcNow;
        await subject.GetExternalIpAsync();
        var after = DateTime.UtcNow;

        var lastFetch = GetLastFetch(subject);
        Assert.That(lastFetch, Is.InRange(before, after));
    }

    [Test]
    public async Task GetExternalIpAsync_should_return_empty_when_all_sources_fail_and_no_prior_cache()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("connection refused"));
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public async Task GetExternalIpAsync_should_return_stale_cache_when_all_sources_fail()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("connection refused"));
        var subject = new ExternalIpService(new HttpClient(handler));
        SetCache(subject, "99.88.77.66", DateTime.UtcNow.AddMinutes(-15));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("99.88.77.66"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_return_empty_when_all_sources_return_invalid_ip()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "bad");
        handler.Enqueue(HttpStatusCode.OK, "bad");
        handler.Enqueue(HttpStatusCode.OK, "bad");
        handler.Enqueue(HttpStatusCode.OK, "bad");
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public async Task GetExternalIpAsync_should_refetch_when_cache_is_stale()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "50.60.70.80");
        var subject = new ExternalIpService(new HttpClient(handler));
        SetCache(subject, "old.ip.address", DateTime.UtcNow.AddHours(-2));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("50.60.70.80"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_accept_ipv6_address()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "2606:4700:4700::1111");
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("2606:4700:4700::1111"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_accept_cancellation_token()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "1.1.1.1");
        var subject = new ExternalIpService(new HttpClient(handler));
        using var cts = new CancellationTokenSource();

        var result = await subject.GetExternalIpAsync(cts.Token);

        Assert.That(result, Is.EqualTo("1.1.1.1"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_handle_exception_from_source_and_continue()
    {
        // First source throws, second returns a valid IP.
        // ThrowingHttpMessageHandler always throws, so we need a custom approach:
        // enqueue nothing — the MockHttpMessageHandler returns HTTP 500 on empty queue
        // (body = "{}"), which is not a valid IP, so it falls through.
        var handler = new MockHttpMessageHandler();
        // No enqueue: first dequeue → 500 with "{}" → not a valid IP
        // second → 500 with "{}" → not valid, etc.
        // All 4 sources return invalid → return ""
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo(""));
    }

    [Test]
    public async Task GetExternalIpAsync_second_call_uses_cache_without_network()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "4.4.4.4");
        // Only one response enqueued; second call must use cache
        var subject = new ExternalIpService(new HttpClient(handler));

        var first = await subject.GetExternalIpAsync();
        var second = await subject.GetExternalIpAsync();

        Assert.That(first, Is.EqualTo("4.4.4.4"));
        Assert.That(second, Is.EqualTo("4.4.4.4"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_query_seedarr_net_with_uuid_and_extract_ip_from_json()
    {
        var jsonResponse = @"
{
  ""status"": ""success"",
  ""action"": ""inserted"",
  ""message"": ""Client entry inserted successfully."",
  ""data"": {
    ""uuid"": ""f47ac10b-58cc-4372-a567-0e02b2c3d479"",
    ""ip"": ""93.184.216.34"",
    ""timestamp"": 1756585406
  }
}";
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, jsonResponse);

        var configService = NSubstitute.Substitute.For<NzbDrone.Core.Configuration.IConfigService>();
        configService.InstanceUuid.Returns("f47ac10b-58cc-4372-a567-0e02b2c3d479");

        var subject = new ExternalIpService(configService, new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("93.184.216.34"));
        Assert.That(subject.CachedIp, Is.EqualTo("93.184.216.34"));
    }

    [Test]
    public void TryExtractIpFromResponse_should_parse_seedarr_net_json_response()
    {
        var jsonResponse = @"
{
  ""status"": ""success"",
  ""action"": ""inserted"",
  ""message"": ""Client entry inserted successfully."",
  ""data"": {
    ""uuid"": ""f47ac10b-58cc-4372-a567-0e02b2c3d479"",
    ""ip"": ""142.250.190.46"",
    ""timestamp"": 1756585406
  }
}";
        var success = ExternalIpService.TryExtractIpFromResponse(jsonResponse, out var ip);

        Assert.That(success, Is.True);
        Assert.That(ip, Is.EqualTo("142.250.190.46"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_fallback_to_secondary_source_if_primary_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "error"); // primary https://seedarr.net/ip/?uuid=... fails
        handler.Enqueue(HttpStatusCode.OK, "142.250.190.46");          // fallback succeeds

        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("142.250.190.46"));
    }

    [TestCase("127.0.0.1")]
    [TestCase("127.0.0.2")]
    [TestCase("::1")]
    [TestCase("0.0.0.0")]
    [TestCase("::")]
    [TestCase("10.0.0.1")]
    [TestCase("10.254.254.254")]
    [TestCase("172.16.0.1")]
    [TestCase("172.24.0.1")]
    [TestCase("172.31.255.255")]
    [TestCase("192.168.0.1")]
    [TestCase("192.168.1.1")]
    [TestCase("100.64.0.1")]
    [TestCase("100.127.255.254")]
    [TestCase("169.254.1.1")]
    [TestCase("fe80::1")]
    [TestCase("fe80::200:5aee:feaa:20a2")]
    [TestCase("192.0.2.1")]
    [TestCase("198.51.100.1")]
    [TestCase("203.0.113.1")]
    [TestCase("2001:db8::1")]
    [TestCase("224.0.0.1")]
    [TestCase("239.255.255.250")]
    [TestCase("240.0.0.1")]
    [TestCase("255.255.255.255")]
    [TestCase("ff02::1")]
    [TestCase("fc00::1")]
    [TestCase("fd00::1")]
    [TestCase("fec0::1")]
    public void IsPublicRoutableIpAddress_should_reject_non_public_ips(string ipString)
    {
        var parsed = IPAddress.Parse(ipString);
        var isPublic = ExternalIpService.IsPublicRoutableIpAddress(parsed);

        Assert.That(isPublic, Is.False, $"Expected {ipString} to be rejected as non-public/bogon");
    }

    [TestCase("1.1.1.1")]
    [TestCase("8.8.8.8")]
    [TestCase("93.184.216.34")]
    [TestCase("142.250.190.46")]
    [TestCase("2606:4700:4700::1111")]
    [TestCase("2001:4860:4860::8888")]
    public void IsPublicRoutableIpAddress_should_accept_public_ips(string ipString)
    {
        var parsed = IPAddress.Parse(ipString);
        var isPublic = ExternalIpService.IsPublicRoutableIpAddress(parsed);

        Assert.That(isPublic, Is.True, $"Expected {ipString} to be accepted as public routable");
    }

    [Test]
    public void TryExtractIpFromResponse_should_reject_private_ip_in_plaintext()
    {
        Assert.That(ExternalIpService.TryExtractIpFromResponse("192.168.1.1", out _), Is.False);
        Assert.That(ExternalIpService.TryExtractIpFromResponse("10.0.0.1", out _), Is.False);
        Assert.That(ExternalIpService.TryExtractIpFromResponse("127.0.0.1", out _), Is.False);
        Assert.That(ExternalIpService.TryExtractIpFromResponse("100.64.0.1", out _), Is.False);
        Assert.That(ExternalIpService.TryExtractIpFromResponse("169.254.1.1", out _), Is.False);
        Assert.That(ExternalIpService.TryExtractIpFromResponse("::1", out _), Is.False);
    }

    [Test]
    public void TryExtractIpFromResponse_should_reject_private_ip_in_json()
    {
        var json = @"{ ""data"": { ""ip"": ""192.168.1.1"" } }";
        Assert.That(ExternalIpService.TryExtractIpFromResponse(json, out _), Is.False);

        var loopbackJson = @"{ ""ip"": ""127.0.0.1"" }";
        Assert.That(ExternalIpService.TryExtractIpFromResponse(loopbackJson, out _), Is.False);
    }

    [Test]
    public async Task GetExternalIpAsync_should_allow_concurrent_callers_to_both_obtain_fetched_ip()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "93.184.216.34");
        var subject = new ExternalIpService(new HttpClient(handler));

        var task1 = subject.GetExternalIpAsync();
        var task2 = subject.GetExternalIpAsync();

        var results = await Task.WhenAll(task1, task2);

        Assert.That(results[0], Is.EqualTo("93.184.216.34"));
        Assert.That(results[1], Is.EqualTo("93.184.216.34"));
    }

    [Test]
    public async Task GetExternalIpAsync_when_vpn_fail_closed_active_aborts_without_network_call()
    {
        var configService = Substitute.For<IConfigService>();
        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.IsFailClosedActive.Returns(true);

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "198.51.100.1");

        var subject = new ExternalIpService(configService, vpnService, null, new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo(string.Empty));
        Assert.That(handler.Requests.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetExternalIpAsync_when_vpn_fail_closed_active_returns_existing_cached_ip_without_network_call()
    {
        var configService = Substitute.For<IConfigService>();
        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.IsFailClosedActive.Returns(true);

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "198.51.100.1");

        var subject = new ExternalIpService(configService, vpnService, null, new HttpClient(handler));
        SetCache(subject, "203.0.113.50", DateTime.UtcNow.AddHours(-2));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("203.0.113.50"));
        Assert.That(handler.Requests.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetExternalIpAsync_when_bind_interface_specified_but_ip_cannot_be_resolved_aborts_to_prevent_leak()
    {
        var configService = Substitute.For<IConfigService>();
        configService.BindInterface.Returns("tun0");

        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.IsFailClosedActive.Returns(false);
        vpnService.GetVpnInterfaceIpAddress(Arg.Any<System.Net.Sockets.AddressFamily>()).Returns((IPAddress)null);

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "198.51.100.1");

        var subject = new ExternalIpService(configService, vpnService, null, new HttpClient(handler))
        {
            InterfaceIpResolver = _ => null
        };

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo(string.Empty));
        Assert.That(handler.Requests.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task GetExternalIpAsync_when_force_proxy_active_but_proxy_not_enabled_aborts_to_prevent_leak()
    {
        var configService = Substitute.For<IConfigService>();
        configService.ForceProxy.Returns(true);

        var proxySettings = Substitute.For<IProxySettingsProvider>();
        proxySettings.IsEnabled.Returns(false);

        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "198.51.100.1");

        var subject = new ExternalIpService(configService, null, proxySettings, new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo(string.Empty));
        Assert.That(handler.Requests.Count, Is.EqualTo(0));
    }

    [Test]
    public void CreateHandler_when_bind_interface_specified_and_resolves_ip_configures_connect_callback()
    {
        var configService = Substitute.For<IConfigService>();
        configService.BindInterface.Returns("tun0");

        var subject = new ExternalIpService(configService, null, null)
        {
            InterfaceIpResolver = iface => IPAddress.Parse("10.8.0.2")
        };

        var handler = subject.CreateHandler() as SocketsHttpHandler;

        Assert.That(handler, Is.Not.Null);
        Assert.That(handler.ConnectCallback, Is.Not.Null);
    }

    [Test]
    public void CreateHandler_when_bind_interface_is_direct_ip_configures_connect_callback()
    {
        var configService = Substitute.For<IConfigService>();
        configService.BindInterface.Returns("10.8.0.15");

        var subject = new ExternalIpService(configService, null, null);

        var handler = subject.CreateHandler() as SocketsHttpHandler;

        Assert.That(handler, Is.Not.Null);
        Assert.That(handler.ConnectCallback, Is.Not.Null);
    }

    [TestCase("Any")]
    [TestCase("all")]
    [TestCase("*")]
    [TestCase("0.0.0.0")]
    [TestCase("::")]
    [TestCase("")]
    [TestCase(null)]
    public void CreateHandler_when_bind_interface_is_wildcard_or_empty_connect_callback_is_null(string bindIface)
    {
        var configService = Substitute.For<IConfigService>();
        configService.BindInterface.Returns(bindIface);

        var subject = new ExternalIpService(configService, null, null);

        var handler = subject.CreateHandler() as SocketsHttpHandler;

        Assert.That(handler, Is.Not.Null);
        Assert.That(handler.ConnectCallback, Is.Null);
    }

    [Test]
    public void CreateBoundHandler_creates_sockets_http_handler_with_connect_callback()
    {
        var handler = ExternalIpService.CreateBoundHandler("tun0", IPAddress.Parse("10.8.0.2"));

        Assert.That(handler, Is.Not.Null);
        Assert.That(handler.ConnectCallback, Is.Not.Null);
        Assert.That(handler.PooledConnectionLifetime, Is.EqualTo(TimeSpan.FromMinutes(10)));
    }

    [Test]
    public void CreateHandler_when_proxy_enabled_returns_proxy_handler()
    {
        var configService = Substitute.For<IConfigService>();
        var proxySettings = Substitute.For<IProxySettingsProvider>();
        proxySettings.IsEnabled.Returns(true);

        var expectedHandler = new SocketsHttpHandler();
        proxySettings.CreateHandler().Returns(expectedHandler);

        var subject = new ExternalIpService(configService, null, proxySettings);

        var handler = subject.CreateHandler();

        proxySettings.Received(1).CreateHandler();
        Assert.That(handler, Is.SameAs(expectedHandler));
    }

    [Test]
    public void CreateHandler_when_proxy_enabled_preempts_interface_binding()
    {
        var configService = Substitute.For<IConfigService>();
        configService.BindInterface.Returns("tun0");

        var proxySettings = Substitute.For<IProxySettingsProvider>();
        proxySettings.IsEnabled.Returns(true);

        var expectedHandler = new SocketsHttpHandler();
        proxySettings.CreateHandler().Returns(expectedHandler);

        var subject = new ExternalIpService(configService, null, proxySettings)
        {
            InterfaceIpResolver = _ => IPAddress.Parse("10.8.0.2")
        };

        var handler = subject.CreateHandler();

        proxySettings.Received(1).CreateHandler();
        Assert.That(handler, Is.SameAs(expectedHandler));
    }

    [Test]
    public void ResolveBindIp_resolves_ip_from_vpn_kill_switch_service()
    {
        var configService = Substitute.For<IConfigService>();
        configService.BindInterface.Returns("tun0");

        var vpnService = Substitute.For<IVpnKillSwitchService>();
        vpnService.IsFailClosedActive.Returns(false);
        vpnService.GetVpnInterfaceIpAddress(System.Net.Sockets.AddressFamily.InterNetwork).Returns(IPAddress.Parse("10.8.0.44"));

        var subject = new ExternalIpService(configService, vpnService, null);

        var ip = subject.ResolveBindIp("tun0");

        Assert.That(ip, Is.EqualTo(IPAddress.Parse("10.8.0.44")));
    }
}
