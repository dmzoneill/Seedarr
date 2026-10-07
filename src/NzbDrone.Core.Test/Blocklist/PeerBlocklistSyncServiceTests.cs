using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Blocklist;
using NzbDrone.Core.Configuration;
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
    public async Task SyncAsync_transport_failure_after_success_should_clear_LastSyncHttpStatus()
    {
        var responseOk = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("192.168.1.1\n")
        };
        _mockHandler.EnqueueResponse(responseOk);

        var firstResult = await _service.SyncAsync("http://blocklist.test/rules.txt");
        Assert.That(firstResult.Success, Is.True);
        Assert.That(_service.Metadata.LastSyncHttpStatus, Is.EqualTo(HttpStatusCode.OK));

        _mockHandler.ResponseFactory = _ => throw new HttpRequestException("Connection reset");

        var secondResult = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(secondResult.Success, Is.False);
        Assert.That(_service.Metadata.LastSyncHttpStatus, Is.Null);
        Assert.That(_service.Metadata.LastSyncStatus, Does.StartWith("Failed:"));
        Assert.That(_service.Metadata.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(_service.RuleCount, Is.EqualTo(1));
    }

    [Test]
    public void Default_http_client_timeout_should_be_bounded()
    {
        Assert.That(PeerBlocklistSyncService.DefaultHttpClientTimeout, Is.EqualTo(TimeSpan.FromSeconds(100)));
    }

    [Test]
    public async Task SyncAsync_http_timeout_should_apply_exponential_backoff()
    {
        using var handler = new StallHttpMessageHandler();
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var service = new PeerBlocklistSyncService(
            client,
            configService: null,
            nowProvider: () => _currentTime);

        var result = await service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.False);
        Assert.That(result.IsRateLimited, Is.True);
        Assert.That(service.NextAllowedSyncUtc, Is.EqualTo(_currentTime.AddMinutes(5)));
        Assert.That(service.Metadata.LastSyncHttpStatus, Is.Null);
        Assert.That(service.Metadata.LastSyncStatus, Does.Contain("Timed Out"));
        Assert.That(service.IsSyncAllowed(), Is.False);
    }

    [Test]
    public async Task SyncAsync_should_dispose_http_response_on_success()
    {
        var response = new TrackDisposeHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("192.168.1.1\n")
        };
        _mockHandler.EnqueueResponse(response);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.True);
        Assert.That(response.IsDisposed, Is.True);
    }

    [Test]
    public async Task SyncAsync_should_dispose_http_response_on_304_not_modified()
    {
        var responseOk = new TrackDisposeHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("1.1.1.1\n")
        };
        responseOk.Headers.ETag = new EntityTagHeaderValue("\"etag-v1\"");
        _mockHandler.EnqueueResponse(responseOk);
        await _service.SyncAsync("http://blocklist.test/rules.txt");

        var response304 = new TrackDisposeHttpResponseMessage(HttpStatusCode.NotModified);
        _mockHandler.EnqueueResponse(response304);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.True);
        Assert.That(response304.IsDisposed, Is.True);
    }

    [Test]
    public async Task SyncAsync_should_dispose_http_response_on_rate_limit()
    {
        var response = new TrackDisposeHttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
        _mockHandler.EnqueueResponse(response);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.False);
        Assert.That(response.IsDisposed, Is.True);
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
    public async Task SyncAsync_should_drop_unparsable_banner_lines_from_active_rules_and_rule_count()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Bluetack Level 1 Blocklist\n192.168.1.0/24\n")
        };
        _mockHandler.EnqueueResponse(response);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.True);
        Assert.That(result.RuleCount, Is.EqualTo(1));
        Assert.That(_service.ActiveRules.Count, Is.EqualTo(1));
        Assert.That(_service.ActiveRules[0], Is.EqualTo("192.168.1.0/24"));
        Assert.That(_service.IsBlocked("192.168.1.50"), Is.True);
    }

    [Test]
    public void SetActiveRules_should_drop_unparsable_lines_from_active_rules_and_rule_count()
    {
        _service.SetActiveRules(new[] { "Bluetack Level 1 Blocklist", "192.168.1.0/24" });

        Assert.That(_service.RuleCount, Is.EqualTo(1));
        Assert.That(_service.ActiveRules, Is.EqualTo(new[] { "192.168.1.0/24" }));
    }

    [Test]
    public async Task SyncAsync_after_blocklist_url_change_should_not_send_stale_conditional_headers()
    {
        var feedA = "http://blocklist-a.test/rules.txt";
        var feedB = "http://blocklist-b.test/rules.txt";

        var responseA = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("10.0.0.0/8\n")
        };
        responseA.Headers.ETag = new EntityTagHeaderValue("\"etag-feed-a\"");
        _mockHandler.EnqueueResponse(responseA);

        await _service.SyncAsync(feedA);
        Assert.That(_service.IsBlocked("10.0.0.1"), Is.True);

        var responseB = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("192.168.0.0/16\n")
        };
        responseB.Headers.ETag = new EntityTagHeaderValue("\"etag-feed-b\"");
        _mockHandler.EnqueueResponse(responseB);

        var result = await _service.SyncAsync(feedB);

        Assert.That(result.Success, Is.True);
        Assert.That(_service.IsBlocked("192.168.1.1"), Is.True);
        Assert.That(_service.IsBlocked("10.0.0.1"), Is.False);
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(2));

        var secondRequest = _mockHandler.Requests[1];
        Assert.That(secondRequest.RequestUri!.ToString(), Is.EqualTo(feedB));
        Assert.That(secondRequest.Headers.Contains("If-None-Match"), Is.False);
        Assert.That(secondRequest.Headers.IfModifiedSince, Is.Null);
        Assert.That(_service.Metadata.BlocklistValidatorUrl, Is.EqualTo(feedB));
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
    public async Task SyncAsync_when_etag_and_last_modified_cached_in_config_but_tree_empty_should_not_send_conditional_headers()
    {
        var configService = Substitute.For<IConfigService>();
        configService.BlocklistETag.Returns("\"persisted-etag\"");
        configService.BlocklistLastModified.Returns("Sat, 19 Sep 2026 08:00:00 GMT");

        using var handler = new MockHttpMessageHandler();
        using var client = new HttpClient(handler);
        var service = new PeerBlocklistSyncService(
            client,
            configService: configService,
            nowProvider: () => _currentTime);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("192.168.1.1\n")
        };
        handler.EnqueueResponse(response);

        var result = await service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.True);
        Assert.That(result.RuleCount, Is.EqualTo(1));
        Assert.That(service.IsBlocked("192.168.1.1"), Is.True);
        Assert.That(handler.Requests.Count, Is.EqualTo(1));

        var request = handler.Requests[0];
        Assert.That(request.Headers.Contains("If-None-Match"), Is.False);
        Assert.That(request.Headers.IfModifiedSince, Is.Null);
    }

    [Test]
    public async Task SyncAsync_when_server_returns_304_and_tree_is_empty_should_fallback_to_unconditional_request()
    {
        var response304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        var responseOk = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("10.0.0.1\n")
        };
        _mockHandler.EnqueueResponse(response304);
        _mockHandler.EnqueueResponse(responseOk);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.True);
        Assert.That(result.IsNotModified, Is.False);
        Assert.That(result.RuleCount, Is.EqualTo(1));
        Assert.That(_service.IsBlocked("10.0.0.1"), Is.True);
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(2));

        var fallbackRequest = _mockHandler.Requests[1];
        Assert.That(fallbackRequest.Headers.Contains("If-None-Match"), Is.False);
        Assert.That(fallbackRequest.Headers.IfModifiedSince, Is.Null);
    }

    [Test]
    public async Task SyncAsync_when_server_returns_304_twice_with_empty_tree_should_fail_without_reporting_success()
    {
        var response304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        _mockHandler.EnqueueResponse(response304);
        _mockHandler.EnqueueResponse(response304);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.False);
        Assert.That(result.IsNotModified, Is.True);
        Assert.That(result.RuleCount, Is.EqualTo(0));
        Assert.That(_service.RuleCount, Is.EqualTo(0));
        Assert.That(_service.IsBlocked("10.0.0.1"), Is.False);
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(2));
        Assert.That(_service.Metadata.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(_service.Metadata.LastSyncStatus, Does.Contain("no blocklist loaded"));
    }

    [Test]
    public async Task SyncAsync_force_true_should_not_send_conditional_headers_even_when_tree_is_populated()
    {
        var response1 = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("192.168.1.1\n")
        };
        response1.Headers.ETag = new EntityTagHeaderValue("\"etag-v1\"");
        response1.Content.Headers.LastModified = new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero);
        _mockHandler.EnqueueResponse(response1);

        await _service.SyncAsync("http://blocklist.test/rules.txt");

        var responseForced = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("192.168.1.1\n10.0.0.1\n")
        };
        _mockHandler.EnqueueResponse(responseForced);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt", force: true);

        Assert.That(result.Success, Is.True);
        Assert.That(_mockHandler.Requests.Count, Is.EqualTo(2));

        var forcedRequest = _mockHandler.Requests[1];
        Assert.That(forcedRequest.Headers.Contains("If-None-Match"), Is.False);
        Assert.That(forcedRequest.Headers.IfModifiedSince, Is.Null);
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
    public async Task SyncAsync_quota_exceeded_should_apply_exponential_backoff_and_defer_next_sync()
    {
        var streamProvider = Substitute.For<IBlocklistArchiveStreamProvider>();
        streamProvider
            .ExtractRulesAsync(
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<List<string>>>(_ => throw new BlocklistQuotaExceededException("Blocklist exceeds size quota"));

        using var handler = new MockHttpMessageHandler();
        using var client = new HttpClient(handler);
        var service = new PeerBlocklistSyncService(
            client,
            configService: null,
            nowProvider: () => _currentTime,
            streamProvider: streamProvider);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ignored")
        };
        handler.EnqueueResponse(response);

        var firstResult = await service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(firstResult.Success, Is.False);
        Assert.That(firstResult.IsRateLimited, Is.True);
        Assert.That(service.NextAllowedSyncUtc, Is.EqualTo(_currentTime.AddMinutes(5)));
        Assert.That(service.IsSyncAllowed(), Is.False);
        Assert.That(service.Metadata.LastSyncStatus, Does.Contain("Quota Exceeded"));
        Assert.That(handler.Requests.Count, Is.EqualTo(1));

        var secondResult = await service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(secondResult.Success, Is.False);
        Assert.That(secondResult.IsRateLimited, Is.True);
        Assert.That(handler.Requests.Count, Is.EqualTo(1));
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
        Assert.That(_service.LastCheckedUtc, Is.EqualTo(_currentTime));
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
    public async Task SyncAsync_200_OK_with_no_enforceable_rules_should_retain_prior_blocklist()
    {
        _service.SetActiveRules(new[] { "10.0.0.0/8" });
        Assert.That(_service.IsBlocked("10.0.0.1"), Is.True);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-an-ip-range\n<html><body>error</body></html>")
        };
        _mockHandler.EnqueueResponse(response);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.False);
        Assert.That(result.HttpStatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(result.Status, Does.StartWith("Failed:"));
        Assert.That(_service.RuleCount, Is.EqualTo(1));
        Assert.That(_service.ActiveRules[0], Is.EqualTo("10.0.0.0/8"));
        Assert.That(_service.IsBlocked("10.0.0.1"), Is.True);
        Assert.That(_service.Metadata.ConsecutiveFailures, Is.EqualTo(1));
    }

    [Test]
    public async Task SyncAsync_200_OK_with_empty_body_should_succeed_when_no_prior_rules()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty)
        };
        _mockHandler.EnqueueResponse(response);

        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        Assert.That(result.Success, Is.True);
        Assert.That(result.RuleCount, Is.EqualTo(0));
        Assert.That(_service.RuleCount, Is.EqualTo(0));
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
        using var started = new ManualResetEventSlim(false);

        var reader = Task.Run(() =>
        {
            var testIp = IPAddress.Parse("10.10.1.1");
            started.Set();
            while (!cts.Token.IsCancellationRequested)
            {
                _service.IsBlocked(testIp);
                Interlocked.Increment(ref readCount);
            }
        });

        started.Wait(TimeSpan.FromSeconds(5));
        var result = await _service.SyncAsync("http://blocklist.test/rules.txt");

        await cts.CancelAsync();
        await reader;

        Assert.That(result.Success, Is.True);
        Assert.That(_service.IsBlocked("10.10.1.1"), Is.True);
        Assert.That(_service.IntervalTree, Is.Not.Null);
        Assert.That(_service.IntervalTree.IntervalCount, Is.GreaterThan(0));
        Assert.That(readCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task SyncAsync_overlapping_calls_should_not_apply_stale_rules_when_slow_sync_finishes_last()
    {
        var slowRelease = new ManualResetEventSlim(false);
        var slowStarted = new ManualResetEventSlim(false);
        var requestCount = 0;

        _mockHandler.ResponseFactory = _ =>
        {
            var count = Interlocked.Increment(ref requestCount);
            if (count == 1)
            {
                slowStarted.Set();
                slowRelease.Wait(TimeSpan.FromSeconds(5));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("10.0.0.0/8\n")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("192.168.0.0/16\n")
            };
        };

        var url = "http://blocklist.test/rules.txt";
        var slowSync = _service.SyncAsync(url);
        Assert.That(slowStarted.Wait(TimeSpan.FromSeconds(5)), Is.True, "slow sync should start HTTP before fast sync runs");

        var fastSync = _service.SyncAsync(url);
        slowRelease.Set();

        await Task.WhenAll(slowSync, fastSync);

        Assert.That(_service.IsBlocked("192.168.1.1"), Is.True);
        Assert.That(_service.IsBlocked("10.0.0.1"), Is.False);
        Assert.That(requestCount, Is.EqualTo(2));
    }

    private sealed class StallHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class TrackDisposeHttpResponseMessage : HttpResponseMessage
    {
        public bool IsDisposed { get; private set; }

        public TrackDisposeHttpResponseMessage(HttpStatusCode statusCode)
            : base(statusCode)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
