using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Notifications;

[TestFixture]
public class WebhookDispatcherTest
{
    [TestCase("http://10.0.0.1/webhook")]
    [TestCase("http://10.255.255.255/webhook")]
    [TestCase("http://172.16.0.1/webhook")]
    [TestCase("http://172.31.255.255/webhook")]
    [TestCase("http://192.168.0.1/webhook")]
    [TestCase("http://192.168.1.100/webhook")]
    [TestCase("https://10.0.0.1:8443/webhook")]
    public void IsValidTargetUrl_should_reject_rfc1918_private_ips(string url)
    {
        Assert.That(WebhookDispatcher.IsValidTargetUrl(url), Is.False);
    }

    [TestCase("http://100.64.0.1/webhook")]
    [TestCase("http://100.127.255.255/webhook")]
    public void IsValidTargetUrl_should_reject_cgnat_ips(string url)
    {
        Assert.That(WebhookDispatcher.IsValidTargetUrl(url), Is.False);
    }

    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("http://169.254.1.1/api")]
    [TestCase("http://metadata.google.internal/computeMetadata/v1/")]
    [TestCase("http://instance-data/latest/meta-data")]
    [TestCase("https://metadata.google.internal/api")]
    public void IsValidTargetUrl_should_reject_cloud_metadata_and_link_local(string url)
    {
        Assert.That(WebhookDispatcher.IsValidTargetUrl(url), Is.False);
    }

    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("http://metadata.google.internal/computeMetadata/v1/")]
    [TestCase("http://instance-data/latest/meta-data")]
    [TestCase("http://10.0.0.1/webhook")]
    [TestCase("http://192.168.1.1/webhook")]
    [TestCase("http://172.16.0.1/webhook")]
    [TestCase("http://100.64.0.1/webhook")]
    [TestCase("http://[fd00::1]/webhook")]
    public void IsValidTargetUrl_with_allowLoopback_true_should_still_reject_private_and_metadata(string url)
    {
        Assert.That(WebhookDispatcher.IsValidTargetUrl(url, allowLoopback: true), Is.False);
    }

    [TestCase("http://127.0.0.1/webhook")]
    [TestCase("http://localhost/webhook")]
    [TestCase("http://localhost:5000/webhook")]
    [TestCase("http://[::1]/webhook")]
    [TestCase("http://test.localhost/webhook")]
    public void IsValidTargetUrl_with_allowLoopback_false_should_reject_loopback(string url)
    {
        Assert.That(WebhookDispatcher.IsValidTargetUrl(url, allowLoopback: false), Is.False);
    }

    [TestCase("http://127.0.0.1/webhook")]
    [TestCase("http://localhost/webhook")]
    [TestCase("http://localhost:5000/webhook")]
    [TestCase("http://[::1]/webhook")]
    [TestCase("http://test.localhost/webhook")]
    public void IsValidTargetUrl_with_allowLoopback_true_should_permit_loopback(string url)
    {
        Assert.That(WebhookDispatcher.IsValidTargetUrl(url, allowLoopback: true), Is.True);
    }

    [TestCase("http://8.8.8.8/webhook")]
    [TestCase("https://8.8.4.4/webhook")]
    [TestCase("http://[2606:4700:4700::1111]/webhook")]
    public void IsValidTargetUrl_should_permit_public_ips(string url)
    {
        Assert.That(WebhookDispatcher.IsValidTargetUrl(url), Is.True);
    }

    [TestCase("ftp://8.8.8.8/webhook")]
    [TestCase("udp://8.8.8.8:1234")]
    [TestCase("javascript:alert(1)")]
    [TestCase("file:///etc/passwd")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void IsValidTargetUrl_should_reject_invalid_schemes_and_empty(string url)
    {
        Assert.That(WebhookDispatcher.IsValidTargetUrl(url), Is.False);
    }

    [Test]
    public void SharedHandler_should_have_AllowAutoRedirect_disabled()
    {
        Assert.That(WebhookDispatcher.SharedHandler.AllowAutoRedirect, Is.False);
    }

    [Test]
    public void ExtractRetryAfter_should_clamp_headers_to_60_seconds_maximum()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "120");

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        Assert.That(retryAfter, Is.Not.Null);
        Assert.That(retryAfter.Value, Is.EqualTo(TimeSpan.FromSeconds(60)));
    }

    [Test]
    public void ExtractRetryAfter_should_preserve_delay_under_60_seconds()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "25");

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        Assert.That(retryAfter, Is.Not.Null);
        Assert.That(retryAfter.Value, Is.EqualTo(TimeSpan.FromSeconds(25)));
    }

    [Test]
    public void ExtractRetryAfter_should_clamp_json_body_retry_after_to_60_seconds()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"parameters\":{\"retry_after\":180}}", System.Text.Encoding.UTF8, "application/json")
        };

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        Assert.That(retryAfter, Is.Not.Null);
        Assert.That(retryAfter.Value, Is.EqualTo(TimeSpan.FromSeconds(60)));
    }

    [Test]
    public void ExtractRetryAfter_should_preserve_json_body_retry_after_under_60_seconds()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"parameters\":{\"retry_after\":12}}", System.Text.Encoding.UTF8, "application/json")
        };

        var retryAfter = WebhookDispatcher.ExtractRetryAfter(response);

        Assert.That(retryAfter, Is.Not.Null);
        Assert.That(retryAfter.Value, Is.EqualTo(TimeSpan.FromSeconds(12)));
    }

    [Test]
    public void ExtractRetryAfter_should_return_null_for_null_response()
    {
        Assert.That(WebhookDispatcher.ExtractRetryAfter(null), Is.Null);
    }

    [Test]
    public void ClampRetryAfter_should_clamp_excessive_delays()
    {
        Assert.That(WebhookDispatcher.ClampRetryAfter(TimeSpan.FromSeconds(120)), Is.EqualTo(TimeSpan.FromSeconds(60)));
        Assert.That(WebhookDispatcher.ClampRetryAfter(TimeSpan.FromMinutes(10)), Is.EqualTo(TimeSpan.FromSeconds(60)));
        Assert.That(WebhookDispatcher.ClampRetryAfter(TimeSpan.FromSeconds(30)), Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(WebhookDispatcher.ClampRetryAfter(TimeSpan.FromSeconds(-5)), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task DispatchDetailedAsync_should_block_ssrf_urls_without_sending_request()
    {
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: false);

        var result = await dispatcher.DispatchDetailedAsync("http://192.168.1.1/webhook", new { test = true });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("SSRF protection"));
        Assert.That(handler.Requests.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task DispatchDetailedAsync_should_block_cloud_metadata_even_with_allowLoopback()
    {
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var result = await dispatcher.DispatchDetailedAsync("http://169.254.169.254/latest/meta-data", new { test = true });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("SSRF protection"));
        Assert.That(handler.Requests.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task DispatchDetailedAsync_should_not_follow_redirect_when_auto_redirect_disabled()
    {
        var handler = new MockHttpMessageHandler();
        handler.EnqueueWithHeaders(HttpStatusCode.Found, "", new Dictionary<string, string>
        {
            { "Location", "http://169.254.169.254/latest/meta-data" }
        });

        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var result = await dispatcher.DispatchDetailedAsync("http://127.0.0.1/webhook", new { test = true });

        Assert.That(result.Success, Is.False);
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.Found));
        Assert.That(handler.Requests.Count, Is.EqualTo(1));
    }

    [Test]
    public void SanitizeUrlForLogging_should_redact_telegram_bot_token()
    {
        var url = "https://api.telegram.org" + "/bot123456789:ABCdefGHIjklMNOpqrsTUVwxyz123456/sendMessage";
        var sanitized = WebhookDispatcher.SanitizeUrlForLogging(url);
        Assert.That(sanitized, Is.EqualTo("https://api.telegram.org/bot[REDACTED]/sendMessage"));
    }

    [Test]
    public void SanitizeUrlForLogging_should_redact_discord_webhook_token_while_keeping_webhook_id()
    {
        var url = "https://discord.com" + "/api/webhooks/9876543210/abcdef_12345-XYZ";
        var sanitized = WebhookDispatcher.SanitizeUrlForLogging(url);
        Assert.That(sanitized, Is.EqualTo("https://discord.com/api/webhooks/9876543210/[REDACTED]"));
    }

    [Test]
    public void SanitizeUrlForLogging_should_redact_slack_webhook_path()
    {
        var url = "https://hooks.slack.com" + "/services/T00000000/B00000000/SECRETTOKEN12345";
        var sanitized = WebhookDispatcher.SanitizeUrlForLogging(url);
        Assert.That(sanitized, Is.EqualTo("https://hooks.slack.com/services/[REDACTED]"));
    }

    [Test]
    public void SanitizeUrlForLogging_should_return_empty_for_null_or_whitespace()
    {
        Assert.That(WebhookDispatcher.SanitizeUrlForLogging(null), Is.EqualTo(string.Empty));
        Assert.That(WebhookDispatcher.SanitizeUrlForLogging(""), Is.EqualTo(string.Empty));
        Assert.That(WebhookDispatcher.SanitizeUrlForLogging("   "), Is.EqualTo(string.Empty));
    }

    [Test]
    public void SanitizeUrlForLogging_should_leave_unredacted_urls_unchanged()
    {
        var url = "https://example.com/api/notify?channel=alerts";
        var sanitized = WebhookDispatcher.SanitizeUrlForLogging(url);
        Assert.That(sanitized, Is.EqualTo(url));
    }

    [Test]
    public async Task DispatchDetailedAsync_when_telegram_returns_cant_parse_entities_retries_without_parse_mode_and_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: can't parse entities: Character '.' is reserved and must be escaped with the preceding '\\'"}""");
        handler.Enqueue(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":123}}""");

        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = "123456",
            ["text"] = "*Seedarr* Test message with dot.",
            ["parse_mode"] = "Markdown",
        };

        var result = await dispatcher.DispatchDetailedAsync("https://api.telegram.org/bot123456:ABC/sendMessage", payload);

        Assert.That(result.Success, Is.True);
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(handler.Requests.Count, Is.EqualTo(2));

        var firstBody = await handler.Requests[0].Content.ReadAsStringAsync();
        Assert.That(firstBody, Does.Contain("parse_mode"));

        var secondBody = await handler.Requests[1].Content.ReadAsStringAsync();
        Assert.That(secondBody, Does.Not.Contain("parse_mode"));
        Assert.That(secondBody, Does.Contain("123456"));
    }

    [Test]
    public async Task DispatchDetailedAsync_when_telegram_returns_cant_parse_entities_and_fallback_fails_returns_failure()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"ok":false,"description":"Bad Request: can't parse entities"}""");
        handler.Enqueue(HttpStatusCode.BadRequest, """{"ok":false,"description":"Bad Request: chat not found"}""");

        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = "invalid_chat",
            ["text"] = "Test",
            ["parse_mode"] = "Markdown",
        };

        var result = await dispatcher.DispatchDetailedAsync("https://api.telegram.org/bot123456:ABC/sendMessage", payload);

        Assert.That(result.Success, Is.False);
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(handler.Requests.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task DispatchDetailedAsync_when_400_without_cant_parse_entities_does_not_retry()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, """{"ok":false,"description":"Bad Request: chat not found"}""");

        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = "invalid_chat",
            ["text"] = "Test",
            ["parse_mode"] = "Markdown",
        };

        var result = await dispatcher.DispatchDetailedAsync("https://api.telegram.org/bot123456:ABC/sendMessage", payload);

        Assert.That(result.Success, Is.False);
        Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(handler.Requests.Count, Is.EqualTo(1));
    }

    [Test]
    public void RemoveParseMode_should_remove_parse_mode_from_dictionary_and_anonymous_types()
    {
        var dictPayload = new Dictionary<string, object>
        {
            ["chat_id"] = "123",
            ["text"] = "hello",
            ["parse_mode"] = "Markdown",
        };

        var strippedDict = WebhookDispatcher.RemoveParseMode(dictPayload) as Dictionary<string, object>;
        Assert.That(strippedDict, Is.Not.Null);
        Assert.That(strippedDict.ContainsKey("parse_mode"), Is.False);
        Assert.That(strippedDict["chat_id"], Is.EqualTo("123"));
        Assert.That(strippedDict["text"], Is.EqualTo("hello"));

        var anonPayload = new { chat_id = "456", text = "world", parse_mode = "MarkdownV2" };
        var strippedAnon = WebhookDispatcher.RemoveParseMode(anonPayload) as Dictionary<string, object>;
        Assert.That(strippedAnon, Is.Not.Null);
        Assert.That(strippedAnon.ContainsKey("parse_mode"), Is.False);
    }

    [Test]
    public async Task DispatchDetailedAsync_with_httpMethod_put_sends_put_request()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");

        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var result = await dispatcher.DispatchDetailedAsync(
            "http://127.0.0.1/webhook",
            new { test = true },
            customHeadersJson: null,
            httpMethod: HttpMethod.Put);

        Assert.That(result.Success, Is.True);
        Assert.That(handler.Requests.Count, Is.EqualTo(1));
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Put));
    }

    [Test]
    public async Task DispatchDetailedAsync_with_default_method_sends_post_request()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");

        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var result = await dispatcher.DispatchDetailedAsync(
            "http://127.0.0.1/webhook",
            new { test = true });

        Assert.That(result.Success, Is.True);
        Assert.That(handler.Requests.Count, Is.EqualTo(1));
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Post));
    }

    [Test]
    public async Task DispatchDetailedAsync_with_string_payload_does_not_double_serialize_json()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");

        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var jsonPayload = "{\"event\":\"OnGrab\",\"name\":\"Movie\"}";
        var result = await dispatcher.DispatchDetailedAsync(
            "http://127.0.0.1/webhook",
            jsonPayload);

        Assert.That(result.Success, Is.True);
        Assert.That(handler.Requests.Count, Is.EqualTo(1));

        var sentBody = await handler.LastRequest.Content.ReadAsStringAsync();
        Assert.That(sentBody, Is.EqualTo(jsonPayload));
        Assert.That(handler.LastRequest.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
    }

    [Test]
    public async Task DispatchDetailedAsync_with_basic_auth_custom_headers_attaches_authorization_header()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{}");

        var client = new HttpClient(handler);
        var dispatcher = new WebhookDispatcher(client, timeout: TimeSpan.FromSeconds(5), allowLoopback: true);

        var headersJson = "{\"Authorization\": \"Basic YWRtaW46cGFzc3dvcmQxMjM=\"}";
        var result = await dispatcher.DispatchDetailedAsync(
            "http://127.0.0.1/webhook",
            new { test = true },
            customHeadersJson: headersJson);

        Assert.That(result.Success, Is.True);
        Assert.That(handler.LastRequest.Headers.Authorization, Is.Not.Null);
        Assert.That(handler.LastRequest.Headers.Authorization.Scheme, Is.EqualTo("Basic"));
        Assert.That(handler.LastRequest.Headers.Authorization.Parameter, Is.EqualTo("YWRtaW46cGFzc3dvcmQxMjM="));
    }
}
