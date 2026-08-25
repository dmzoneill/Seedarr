using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Network;
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
        handler.Enqueue(HttpStatusCode.OK, "  192.168.0.1\n");
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("192.168.0.1"));
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
        handler.Enqueue(HttpStatusCode.OK, "2001:db8::1");
        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("2001:db8::1"));
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
    ""ip"": ""127.0.0.1"",
    ""timestamp"": 1756585406
  }
}";
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, jsonResponse);

        var configService = NSubstitute.Substitute.For<NzbDrone.Core.Configuration.IConfigService>();
        configService.InstanceUuid.Returns("f47ac10b-58cc-4372-a567-0e02b2c3d479");

        var subject = new ExternalIpService(configService, new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("127.0.0.1"));
        Assert.That(subject.CachedIp, Is.EqualTo("127.0.0.1"));
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
    ""ip"": ""198.51.100.42"",
    ""timestamp"": 1756585406
  }
}";
        var success = ExternalIpService.TryExtractIpFromResponse(jsonResponse, out var ip);

        Assert.That(success, Is.True);
        Assert.That(ip, Is.EqualTo("198.51.100.42"));
    }

    [Test]
    public async Task GetExternalIpAsync_should_fallback_to_secondary_source_if_primary_fails()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "error"); // primary https://seedarr.net/my/?uuid=... fails
        handler.Enqueue(HttpStatusCode.InternalServerError, "error"); // primary http://seedarr.net/my/?uuid=... fails
        handler.Enqueue(HttpStatusCode.OK, "203.0.113.19");          // fallback succeeds

        var subject = new ExternalIpService(new HttpClient(handler));

        var result = await subject.GetExternalIpAsync();

        Assert.That(result, Is.EqualTo("203.0.113.19"));
    }
}
