using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Blocklist;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Blocklist;

[TestFixture]
public class PeerBlocklistSyncServiceTests
{
    private MockHttpMessageHandler _mockHandler;
    private HttpClient _httpClient;
    private DateTime _currentTime;
    private PeerBlocklistSyncService _service;

    [SetUp]
    public void SetUp()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
        _currentTime = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
        _service = new PeerBlocklistSyncService(
            _httpClient,
            configService: null,
            nowProvider: () => _currentTime);
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
        _mockHandler.Dispose();
    }

    [Test]
    public async Task SyncAsync_200_OK_should_parse_rules_and_cache_etag_and_last_modified()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("# Comment line\n1.2.3.0/24\n// Another comment\n5.6.7.8-5.6.7.9\n\n10.0.0.1\n")
        };
        response.Headers.ETag = new EntityTagHeaderValue("\"abc123etag\"");
        var lastMod = new DateTimeOffset(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);
        response.Content.Headers.LastModified = lastMod;

        _mockHandler.EnqueueResponse(response);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.True);
        Assert.That(result.RuleCount, Is.EqualTo(3));
        Assert.That(result.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(_service.ActiveRules.Count, Is.EqualTo(3));
        Assert.That(_service.ActiveRules[0], Is.EqualTo("1.2.3.0/24"));
        Assert.That(_service.ActiveRules[1], Is.EqualTo("5.6.7.8-5.6.7.9"));
        Assert.That(_service.ActiveRules[2], Is.EqualTo("10.0.0.1"));

        Assert.That(_service.Metadata.BlocklistETag, Is.EqualTo("\"abc123etag\""));
        Assert.That(_service.Metadata.BlocklistLastModified, Is.EqualTo(lastMod));
        Assert.That(_service.Metadata.LastSyncHttpStatus, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(_service.Metadata.LastSyncStatus, Is.EqualTo("Success"));
        Assert.That(_service.Metadata.LastCheckedUtc, Is.EqualTo(_currentTime));
        Assert.That(_service.Metadata.NextAllowedSyncUtc, Is.Null);
        Assert.That(_service.Metadata.ConsecutiveFailures, Is.EqualTo(0));
    }

    [Test]
    public async Task SyncAsync_should_transmit_conditional_headers_on_subsequent_request()
    {
        var response1 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("192.168.1.1\n")
        };
        response1.Headers.ETag = new EntityTagHeaderValue("\"etag-v1\"");
        var lastMod = new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);
        response1.Content.Headers.LastModified = lastMod;
        _mockHandler.EnqueueResponse(response1);

        await _service.SyncAsync("http://blocklist.test/rules.txt");

        var response2 = new HttpResponseMessage(HttpStatusCode.NotModified);
        _mockHandler.EnqueueResponse(response2);

        await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(2));
        var secondRequest = _mockHandler.Requests[1];

        Assert.That(secondRequest.Headers.Contains("If-None-Match"), Is.True);
        Assert.That(secondRequest.Headers.GetValues("If-None-Match").First(), Is.EqualTo("\"etag-v1\""));
        Assert.That(secondRequest.Headers.IfModifiedSince, Is.EqualTo(lastMod));
    }

    [Test]
    public async Task SyncAsync_304_NotModified_should_exit_early_retain_rules_and_update_last_checked()
    {
        var response1 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("1.1.1.1\n2.2.2.2\n")
        };
        response1.Headers.ETag = new EntityTagHeaderValue("\"etag-original\"");
        _mockHandler.EnqueueResponse(response1);
        await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(_service.RuleCount, Is.EqualTo(2));

        _currentTime = _currentTime.AddMinutes(30);

        var response2 = new HttpResponseMessage(HttpStatusCode.NotModified);
        _mockHandler.EnqueueResponse(response2);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.True);
        Assert.That(result.IsNotModified, Is.True);
        Assert.That(result.HttpStatusCode, Is.EqualTo(HttpStatusCode.NotModified));
        Assert.That(result.RuleCount, Is.EqualTo(2));

        Assert.That(_service.RuleCount, Is.EqualTo(2));
        Assert.That(_service.ActiveRules.Count, Is.EqualTo(2));
        Assert.That(_service.ActiveRules[0], Is.EqualTo("1.1.1.1"));
        Assert.That(_service.ActiveRules[1], Is.EqualTo("2.2.2.2"));

        Assert.That(_service.LastSyncStatus, Is.EqualTo("Sync Skipped (Not Modified)"));
        Assert.That(_service.LastSyncHttpStatus, Is.EqualTo(HttpStatusCode.NotModified));
        Assert.That(_service.LastCheckedUtc, Is.EqualTo(_currentTime));
        Assert.That(_service.Metadata.ConsecutiveFailures, Is.EqualTo(0));
    }

    [Test]
    public async Task SyncAsync_429_with_Retry_After_seconds_should_defer_next_sync()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
        _mockHandler.EnqueueResponse(response);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.False);
        Assert.That(result.IsRateLimited, Is.True);
        Assert.That(result.HttpStatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));

        var expectedNext = _currentTime.AddSeconds(120);
        Assert.That(_service.NextAllowedSyncUtc, Is.EqualTo(expectedNext));
        Assert.That(_service.IsSyncAllowed(), Is.False);
        Assert.That(_service.LastSyncStatus, Does.Contain("Rate Limited by Provider"));
        Assert.That(_service.Metadata.ConsecutiveFailures, Is.EqualTo(1));
    }

    [Test]
    public async Task SyncAsync_503_with_Retry_After_date_should_defer_next_sync()
    {
        var retryDate = _currentTime.AddMinutes(45);
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryDate);
        _mockHandler.EnqueueResponse(response);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.False);
        Assert.That(result.IsRateLimited, Is.True);
        Assert.That(result.HttpStatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(_service.NextAllowedSyncUtc, Is.EqualTo(retryDate));
        Assert.That(_service.IsSyncAllowed(), Is.False);
    }

    [Test]
    public async Task SyncAsync_should_reject_immediate_sync_when_rate_limit_backoff_is_active()
    {
        var response429 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response429.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
        _mockHandler.EnqueueResponse(response429);

        await _service.SyncAsync("http://blocklist.test/rules.txt");
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(1));

        _currentTime = _currentTime.AddMinutes(5);

        var deferredResult = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(deferredResult.Success, Is.False);
        Assert.That(deferredResult.IsRateLimited, Is.True);
        Assert.That(deferredResult.Message, Does.Contain("deferred"));
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(1));

        _currentTime = _currentTime.AddMinutes(6);
        Assert.That(_service.IsSyncAllowed(), Is.True);

        var responseOk = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("10.0.0.1\n")
        };
        _mockHandler.EnqueueResponse(responseOk);

        var allowedResult = await _service.SyncAsync("http://blocklist.test/rules.txt");
        Assert.That(allowedResult.Success, Is.True);
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task SyncAsync_force_true_should_bypass_rate_limit_deferral()
    {
        var response429 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response429.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
        _mockHandler.EnqueueResponse(response429);

        await _service.SyncAsync("http://blocklist.test/rules.txt");
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(1));

        var responseOk = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("10.0.0.1\n")
        };
        _mockHandler.EnqueueResponse(responseOk);

        var forcedResult = await _service.SyncAsync("http://blocklist.test/rules.txt", force: true);

        Assert.That(forcedResult.Success, Is.True);
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task SyncAsync_429_without_Retry_After_should_apply_exponential_backoff()
    {
        var response1 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        _mockHandler.EnqueueResponse(response1);

        await _service.SyncAsync("http://blocklist.test/rules.txt");
        Assert.That(_service.NextAllowedSyncUtc, Is.EqualTo(_currentTime.AddMinutes(5)));

        _currentTime = _currentTime.AddMinutes(5);

        var response2 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        _mockHandler.EnqueueResponse(response2);

        await _service.SyncAsync("http://blocklist.test/rules.txt");
        Assert.That(_service.NextAllowedSyncUtc, Is.EqualTo(_currentTime.AddMinutes(10)));

        _currentTime = _currentTime.AddMinutes(10);

        var response3 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        _mockHandler.EnqueueResponse(response3);

        await _service.SyncAsync("http://blocklist.test/rules.txt");
        Assert.That(_service.NextAllowedSyncUtc, Is.EqualTo(_currentTime.AddMinutes(20)));
    }

    [Test]
    public async Task SyncAsync_should_retain_active_rules_across_errors_and_rate_limits()
    {
        _service.SetActiveRules(new[] { "192.168.1.0/24", "10.0.0.0/8" });
        Assert.That(_service.RuleCount, Is.EqualTo(2));

        var response429 = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response429.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
        _mockHandler.EnqueueResponse(response429);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.False);
        Assert.That(_service.RuleCount, Is.EqualTo(2));
        Assert.That(_service.ActiveRules[0], Is.EqualTo("192.168.1.0/24"));
        Assert.That(_service.ActiveRules[1], Is.EqualTo("10.0.0.0/8"));
    }

    [Test]
    public void CalculateExponentialBackoff_should_cap_at_24_hours()
    {
        var backoff = PeerBlocklistSyncService.CalculateExponentialBackoff(20);
        Assert.That(backoff, Is.EqualTo(TimeSpan.FromHours(24)));
    }

    [Test]
    public async Task SyncAsync_with_no_url_configured_should_return_failure()
    {
        var result = await _service.SyncAsync(null);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Status, Is.EqualTo("No URL Configured"));
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(0));
    }

    [Test]
    public void IsBlocked_should_identify_blocked_and_unblocked_addresses()
    {
        Assert.That(_service.IsBlocked("192.168.1.50"), Is.False);
        Assert.That(_service.IsBlocked(IPAddress.Loopback), Is.False);
        Assert.That(_service.IsBlocked((string)null), Is.False);
        Assert.That(_service.IsBlocked((IPAddress)null), Is.False);
        Assert.That(_service.IsBlocked("invalid-ip"), Is.False);

        _service.SetActiveRules(new[]
        {
            "192.168.1.0/24",
            "10.0.0.1",
            "2001:db8::/32"
        });

        Assert.That(_service.IsBlocked("192.168.1.50"), Is.True);
        Assert.That(_service.IsBlocked(IPAddress.Parse("192.168.1.50")), Is.True);
        Assert.That(_service.IsBlocked("192.168.2.1"), Is.False);
        Assert.That(_service.IsBlocked(IPAddress.Parse("192.168.2.1")), Is.False);
        Assert.That(_service.IsBlocked("10.0.0.1"), Is.True);
        Assert.That(_service.IsBlocked("10.0.0.2"), Is.False);
        Assert.That(_service.IsBlocked("2001:db8::1"), Is.True);
        Assert.That(_service.IsBlocked("2001:db9::1"), Is.False);
    }

    [Test]
    public void IsBlocked_and_SetActiveRules_under_concurrent_stress_should_be_lock_free_and_thread_safe()
    {
        var rulesA = new[] { "192.168.1.0/24", "10.0.0.1", "2001:db8::/32" };
        var rulesB = new[] { "172.16.0.0/16", "10.0.0.2", "2001:db9::/32" };

        _service.SetActiveRules(rulesA);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var token = cts.Token;

        var ipBlockedUnderA = IPAddress.Parse("192.168.1.100");
        var ipBlockedUnderB = IPAddress.Parse("172.16.1.1");
        var ipNeverBlocked = IPAddress.Parse("8.8.8.8");

        var readerErrors = 0;
        var readerIterations = 0;

        // Start multiple reader tasks hammering IsBlocked
        var readerTasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            var count = 0;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _service.IsBlocked(ipBlockedUnderA);
                    _service.IsBlocked(ipBlockedUnderB);
                    var blockedNever = _service.IsBlocked(ipNeverBlocked);
                    if (blockedNever)
                    {
                        Interlocked.Increment(ref readerErrors);
                    }

                    count++;
                }
                catch
                {
                    Interlocked.Increment(ref readerErrors);
                }
            }

            Interlocked.Add(ref readerIterations, count);
        })).ToArray();

        // Start writer tasks repeatedly swapping active rules
        var writerTasks = Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            var flip = i % 2 == 0;
            while (!token.IsCancellationRequested)
            {
                _service.SetActiveRules(flip ? rulesA : rulesB);
                flip = !flip;
                await Task.Yield();
            }
        })).ToArray();

        Task.WaitAll(readerTasks.Concat(writerTasks).ToArray());

        Assert.That(readerErrors, Is.EqualTo(0), "No exceptions or false positives should occur during concurrent read/write stress.");
        Assert.That(readerIterations, Is.GreaterThan(1000), "Lock-free readers should complete many iterations without contention.");
    }

    [Test]
    public async Task SyncAsync_atomic_swap_under_concurrent_lookups()
    {
        var response1 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("10.10.0.0/16\n2001:db8:beef::/48\n")
        };
        _mockHandler.EnqueueResponse(response1);

        var readCount = 0;
        var cts = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            var testIp = IPAddress.Parse("10.10.1.1");
            while (!cts.Token.IsCancellationRequested)
            {
                _service.IsBlocked(testIp);
                Interlocked.Increment(ref readCount);
            }
        });

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        await cts.CancelAsync();
        await reader;

        Assert.That(result.Success, Is.True);
        Assert.That(_service.IsBlocked("10.10.1.1"), Is.True);
        Assert.That(_service.IntervalTree, Is.Not.Null);
        Assert.That(_service.IntervalTree.IntervalCount, Is.GreaterThan(0));
        Assert.That(readCount, Is.GreaterThan(0));
    }
}
