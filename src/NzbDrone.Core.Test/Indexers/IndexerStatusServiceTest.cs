using System;
using System.Collections.Generic;
using System.Net;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Test.Indexers;

[TestFixture]
public class IndexerStatusServiceTest
{
    private IndexerStatusService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new IndexerStatusService();
    }

    [Test]
    public void IsDisabled_should_return_false_initially()
    {
        Assert.That(_service.IsDisabled(1), Is.False);
    }

    [Test]
    public void RecordFailure_should_disable_indexer_with_exponential_backoff()
    {
        _service.RecordFailure(1, (int)HttpStatusCode.TooManyRequests, "Rate limited");

        Assert.That(_service.IsDisabled(1), Is.True);
        var status = _service.GetStatus(1);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(status.LastStatusCode, Is.EqualTo(429));
        Assert.That(status.DisabledTill, Is.Not.Null);
    }

    [Test]
    public void RecordSuccess_should_reset_indexer_failures_and_recovery()
    {
        _service.RecordFailure(1, (int)HttpStatusCode.TooManyRequests, "Rate limited");
        Assert.That(_service.IsDisabled(1), Is.True);

        _service.RecordSuccess(1);
        Assert.That(_service.IsDisabled(1), Is.False);
        var status = _service.GetStatus(1);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(0));
        Assert.That(status.DisabledTill, Is.Null);
    }

    [Test]
    public void RecordFailure_HTTP_401_403_should_trigger_auth_failure_extended_backoff_and_disable()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        service.RecordFailure(1, (int)HttpStatusCode.Unauthorized, "Invalid API Key");

        Assert.That(service.IsDisabled(1), Is.True);
        Assert.That(service.IsAuthFailed(1), Is.True);

        var status = service.GetStatus(1);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(status.LastStatusCode, Is.EqualTo(401));
        Assert.That(status.IsAuthFailure, Is.True);
        Assert.That(status.DisabledTill, Is.Not.Null);

        // Extended 24-hour backoff with +/- 15% jitter falls between 20.4 and 27.6 hours
        var backoff = status.DisabledTill.Value - currentTime;
        Assert.That(backoff.TotalHours, Is.GreaterThanOrEqualTo(20.4));
        Assert.That(backoff.TotalHours, Is.LessThanOrEqualTo(27.6));

        // Test 403 Forbidden
        service.RecordFailure(2, (int)HttpStatusCode.Forbidden, "Account suspended");
        Assert.That(service.IsDisabled(2), Is.True);
        Assert.That(service.IsAuthFailed(2), Is.True);
    }

    [Test]
    public void RecordFailure_HTTP_429_should_apply_rate_limit_backoff()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        // Default 429 backoff: minimum 1 hour with +/- 15% jitter
        service.RecordFailure(1, (int)HttpStatusCode.TooManyRequests, "Rate limit exceeded");

        Assert.That(service.IsDisabled(1), Is.True);
        Assert.That(service.IsRateLimited(1), Is.True);

        var status1 = service.GetStatus(1);
        Assert.That(status1.IsRateLimited, Is.True);
        var backoff1 = status1.DisabledTill.Value - currentTime;
        Assert.That(backoff1.TotalMinutes, Is.GreaterThanOrEqualTo(51.0));
        Assert.That(backoff1.TotalMinutes, Is.LessThanOrEqualTo(69.0));

        // Explicit retryAfter
        service.RecordFailure(2, (int)HttpStatusCode.TooManyRequests, "Rate limited", retryAfter: TimeSpan.FromSeconds(120));
        var status2 = service.GetStatus(2);
        var backoff2 = status2.DisabledTill.Value - currentTime;
        Assert.That(backoff2.TotalSeconds, Is.GreaterThanOrEqualTo(102.0));
        Assert.That(backoff2.TotalSeconds, Is.LessThanOrEqualTo(138.0));

        // Parsed retryAfter from message
        service.RecordFailure(3, (int)HttpStatusCode.TooManyRequests, "Rate limit reached. Retry-After: 300");
        var status3 = service.GetStatus(3);
        var backoff3 = status3.DisabledTill.Value - currentTime;
        Assert.That(backoff3.TotalSeconds, Is.GreaterThanOrEqualTo(255.0));
        Assert.That(backoff3.TotalSeconds, Is.LessThanOrEqualTo(345.0));
    }

    [Test]
    public void RecordFailure_generic_errors_should_require_consecutive_failure_threshold_before_disabling()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        // Failure 1: transient network error, below threshold (3)
        service.RecordFailure(1, (int)HttpStatusCode.InternalServerError, "Server Error 1");
        Assert.That(service.IsDisabled(1), Is.False);
        var status1 = service.GetStatus(1);
        Assert.That(status1.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(status1.DisabledTill, Is.Null);

        // Failure 2: transient error, below threshold (3)
        service.RecordFailure(1, null, "Connection timeout");
        Assert.That(service.IsDisabled(1), Is.False);
        var status2 = service.GetStatus(1);
        Assert.That(status2.ConsecutiveFailures, Is.EqualTo(2));
        Assert.That(status2.DisabledTill, Is.Null);

        // Failure 3: reaches threshold, indexer is disabled
        service.RecordFailure(1, (int)HttpStatusCode.BadGateway, "Bad Gateway");
        Assert.That(service.IsDisabled(1), Is.True);
        var status3 = service.GetStatus(1);
        Assert.That(status3.ConsecutiveFailures, Is.EqualTo(3));
        Assert.That(status3.DisabledTill, Is.Not.Null);
    }

    [Test]
    public void CalculateBackoff_should_apply_randomized_jitter_within_expected_percentage_range()
    {
        // For generic errors reaching threshold (3), base backoff is 5 minutes (300 seconds)
        var samples = new List<double>();
        for (var i = 0; i < 50; i++)
        {
            var backoff = _service.CalculateBackoff(3, (int)HttpStatusCode.InternalServerError);
            var seconds = backoff.TotalSeconds;
            // +/- 15% range of 300 seconds is [255, 345]
            Assert.That(seconds, Is.GreaterThanOrEqualTo(255.0));
            Assert.That(seconds, Is.LessThanOrEqualTo(345.0));
            samples.Add(seconds);
        }

        // Verify randomized variation: not all samples are identical
        var hasVariation = false;
        for (var i = 1; i < samples.Count; i++)
        {
            if (Math.Abs(samples[i] - samples[0]) > 0.001)
            {
                hasVariation = true;
                break;
            }
        }

        Assert.That(hasVariation, Is.True);
    }

    [Test]
    public void CalculateBackoff_scales_exponentially()
    {
        // Unjittered calculation for effective failures (failureThreshold: 1)
        var service = new IndexerStatusService(() => DateTime.UtcNow, () => 0.5, failureThreshold: 1);

        Assert.That(service.CalculateBackoff(1, applyJitter: false), Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(service.CalculateBackoff(2, applyJitter: false), Is.EqualTo(TimeSpan.FromMinutes(15)));
        Assert.That(service.CalculateBackoff(3, applyJitter: false), Is.EqualTo(TimeSpan.FromMinutes(30)));
        Assert.That(service.CalculateBackoff(4, applyJitter: false), Is.EqualTo(TimeSpan.FromHours(1)));
        Assert.That(service.CalculateBackoff(5, applyJitter: false), Is.EqualTo(TimeSpan.FromHours(2)));
        Assert.That(service.CalculateBackoff(6, applyJitter: false), Is.EqualTo(TimeSpan.FromHours(4)));
        Assert.That(service.CalculateBackoff(7, applyJitter: false), Is.EqualTo(TimeSpan.FromHours(8)));
        Assert.That(service.CalculateBackoff(8, applyJitter: false), Is.EqualTo(TimeSpan.FromHours(24)));
        Assert.That(service.CalculateBackoff(10, applyJitter: false), Is.EqualTo(TimeSpan.FromHours(24)));
    }

    [Test]
    public void IsDisabled_should_decay_consecutive_failures_when_backoff_window_has_elapsed()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime, () => 0.5, failureThreshold: 2);

        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 1");
        currentTime = currentTime.AddMinutes(1);
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 2");

        var statusBefore = service.GetStatus(1);
        Assert.That(statusBefore.ConsecutiveFailures, Is.EqualTo(2));
        Assert.That(service.IsDisabled(1), Is.True);

        // Advance time past the backoff window
        currentTime = currentTime.AddMinutes(16);

        // When IsDisabled is checked after backoff expires, it decays failure and returns false
        Assert.That(service.IsDisabled(1), Is.False);

        var statusAfter = service.GetStatus(1);
        Assert.That(statusAfter.ConsecutiveFailures, Is.EqualTo(1));
        Assert.That(statusAfter.DisabledTill, Is.Null);
    }

    [Test]
    public void DecayFailures_should_reset_failure_metadata_when_decaying_to_zero()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime, () => 0.5, failureThreshold: 1);

        service.RecordFailure(1, (int)HttpStatusCode.BadGateway, "Bad Gateway");
        Assert.That(service.IsDisabled(1), Is.True);

        // Advance time past the 5-minute backoff window
        currentTime = currentTime.AddMinutes(6);

        var status = service.GetStatus(1);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(0));
        Assert.That(status.DisabledTill, Is.Null);
        Assert.That(status.InitialFailure, Is.Null);
        Assert.That(status.LastFailureMessage, Is.Null);
        Assert.That(status.LastStatusCode, Is.Null);
    }

    [Test]
    public void RecordFailure_after_decay_should_increment_from_decayed_count()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime, () => 0.5, failureThreshold: 1);

        // 3 consecutive failures -> 30 min backoff
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 1");
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 2");
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 3");

        Assert.That(service.GetStatus(1).ConsecutiveFailures, Is.EqualTo(3));

        // Advance time past 30 min backoff
        currentTime = currentTime.AddMinutes(31);

        // Next failure occurs after decay
        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail after decay");

        // Failures should have decayed from 3 to 2, then incremented to 3 (not 4)
        var status = service.GetStatus(1);
        Assert.That(status.ConsecutiveFailures, Is.EqualTo(3));
        // Backoff for 3 failures should be 30 minutes, not 1 hour (4 failures)
        Assert.That(status.DisabledTill, Is.EqualTo(currentTime.Add(TimeSpan.FromMinutes(30))));
    }

    [Test]
    public void GetAllStatuses_should_decay_expired_statuses()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime, () => 0.5, failureThreshold: 1);

        service.RecordFailure(1, (int)HttpStatusCode.ServiceUnavailable, "Fail 1");
        service.RecordFailure(2, (int)HttpStatusCode.ServiceUnavailable, "Fail 2");

        currentTime = currentTime.AddMinutes(6);

        var statuses = service.GetAllStatuses();
        Assert.That(statuses[1].ConsecutiveFailures, Is.EqualTo(0));
        Assert.That(statuses[2].ConsecutiveFailures, Is.EqualTo(0));
    }

    [Test]
    public void ParseRetryAfter_should_parse_integer_seconds()
    {
        var res1 = IndexerStatusService.ParseRetryAfter("120");
        Assert.That(res1, Is.EqualTo(TimeSpan.FromSeconds(120)));

        var res2 = IndexerStatusService.ParseRetryAfter("Retry-After: 300");
        Assert.That(res2, Is.EqualTo(TimeSpan.FromSeconds(300)));

        var res3 = IndexerStatusService.ParseRetryAfter("Rate limit exceeded. Retry in 45 seconds");
        Assert.That(res3, Is.EqualTo(TimeSpan.FromSeconds(45)));
    }

    [Test]
    public void ParseRetryAfter_should_parse_http_date()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var targetDate = new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc);
        var httpDate = targetDate.ToString("r");

        var res1 = IndexerStatusService.ParseRetryAfter(httpDate, now);
        Assert.That(res1, Is.EqualTo(TimeSpan.FromMinutes(5)));

        var res2 = IndexerStatusService.ParseRetryAfter($"Retry-After: {httpDate}", now);
        Assert.That(res2, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void ParseRetryAfter_should_return_zero_when_date_in_past()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var pastDate = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var httpDate = pastDate.ToString("r");

        var res = IndexerStatusService.ParseRetryAfter(httpDate, now);
        Assert.That(res, Is.EqualTo(TimeSpan.Zero));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    [TestCase("no-retry-info")]
    public void ParseRetryAfter_should_return_null_when_invalid(string input)
    {
        Assert.That(IndexerStatusService.ParseRetryAfter(input), Is.Null);
    }

    [Test]
    public void RecordRssSync_should_track_last_and_next_sync_time_with_feed_ttl()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        service.RecordRssSync(1, 30);

        var status = service.GetStatus(1);
        Assert.That(status.LastRssSyncTimeUtc, Is.EqualTo(currentTime));
        Assert.That(status.NextRssSyncTimeUtc, Is.EqualTo(currentTime.AddMinutes(30)));
        Assert.That(status.NextSyncTimeUtc, Is.EqualTo(currentTime.AddMinutes(30)));
    }

    [Test]
    public void RecordRssSync_should_enforce_minimum_polling_interval_of_10_minutes()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        // Feed declares 5 minute TTL, but minimum allowed polling interval is 10 minutes
        service.RecordRssSync(1, 5);

        var status = service.GetStatus(1);
        Assert.That(status.NextRssSyncTimeUtc, Is.EqualTo(currentTime.AddMinutes(10)));
    }

    [TestCase(0)]
    [TestCase(-5)]
    [TestCase(null)]
    public void RecordRssSync_should_fallback_to_15_minutes_when_ttl_is_missing_or_nonpositive(int? ttl)
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        service.RecordRssSync(1, ttl);

        var status = service.GetStatus(1);
        Assert.That(status.NextRssSyncTimeUtc, Is.EqualTo(currentTime.AddMinutes(15)));
    }

    [Test]
    public void CanSyncRss_should_skip_automated_sync_during_ttl_cooldown()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        service.RecordRssSync(1, 20);

        // Within cooldown window: automated sync should be rejected
        Assert.That(service.CanSyncRss(1, isManual: false), Is.False);
        Assert.That(service.ShouldSyncIndexer(1, isManual: false), Is.False);

        // Advance past cooldown window
        currentTime = currentTime.AddMinutes(21);

        // After cooldown window: automated sync should be allowed
        Assert.That(service.CanSyncRss(1, isManual: false), Is.True);
        Assert.That(service.ShouldSyncIndexer(1, isManual: false), Is.True);
    }

    [Test]
    public void CanSyncRss_should_allow_manual_sync_to_bypass_ttl_cooldown()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        service.RecordRssSync(1, 30);

        // Automated sync rejected
        Assert.That(service.CanSyncRss(1, isManual: false), Is.False);

        // Manual sync allowed
        Assert.That(service.CanSyncRss(1, isManual: true), Is.True);
        Assert.That(service.ShouldSyncIndexer(1, isManual: true), Is.True);
    }

    [Test]
    public void CanSyncRss_should_skip_disabled_or_rate_limited_indexers()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        service.RecordFailure(1, (int)HttpStatusCode.TooManyRequests, "Rate limit exceeded");

        Assert.That(service.IsDisabled(1), Is.True);
        Assert.That(service.CanSyncRss(1, isManual: false), Is.False);
    }

    [Test]
    public void RecordFailure_HTTP_429_with_http_date_should_calculate_backoff_duration()
    {
        var currentTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new IndexerStatusService(() => currentTime);

        var retryTarget = currentTime.AddMinutes(10);
        var retryAfterHeader = $"Retry-After: {retryTarget.ToString("r")}";

        service.RecordFailure(1, (int)HttpStatusCode.TooManyRequests, retryAfterHeader);

        Assert.That(service.IsDisabled(1), Is.True);
        Assert.That(service.IsRateLimited(1), Is.True);

        var status = service.GetStatus(1);
        Assert.That(status.DisabledTill, Is.Not.Null);

        // Base 10 minutes (600s) with +/- 15% jitter falls between 510s and 690s
        var backoff = status.DisabledTill.Value - currentTime;
        Assert.That(backoff.TotalSeconds, Is.GreaterThanOrEqualTo(510.0));
        Assert.That(backoff.TotalSeconds, Is.LessThanOrEqualTo(690.0));
    }
}
