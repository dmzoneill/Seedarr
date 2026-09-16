using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Webhook;
using NzbDrone.Core.Test.TestHelpers;
using Polly;
using Polly.Retry;

namespace NzbDrone.Core.Test.Notifications.Webhook;

[TestFixture]
public class WebhookNotificationTest
{
    private WebhookNotification _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new WebhookNotification();
    }

    // Use a no-op ResiliencePipeline so tests don't block on retry delays.
    private static WebhookNotification WithHandler(HttpMessageHandler handler)
        => new WebhookNotification(
            new HttpClient(handler),
            new ResiliencePipelineBuilder().Build());

    [Test]
    public void Name_should_return_webhook()
    {
        Assert.That(_subject.Name, Is.EqualTo("Webhook"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_webhook_url_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_webhook_url_is_null()
    {
        _subject.WebhookUrl = null;

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_webhook_url_is_whitespace()
    {
        _subject.WebhookUrl = "   ";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_webhook_url_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_webhook_url_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_webhook_url_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_webhook_url_is_private_ip()
    {
        _subject.WebhookUrl = "http://192.168.1.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_webhook_url_is_loopback()
    {
        _subject.WebhookUrl = "http://127.0.0.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    // --- HTTP send path tests (use injectable constructor) ---

    [Test]
    public void OnTorrentAdded_should_not_throw_when_url_is_valid_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "");
        var subject = WithHandler(handler);
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_url_is_valid_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "");
        var subject = WithHandler(handler);
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_url_is_valid_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "");
        var subject = WithHandler(handler);
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_url_is_valid_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "");
        var subject = WithHandler(handler);
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_http_call_throws()
    {
        var subject = WithHandler(new ThrowingHttpMessageHandler(
            new HttpRequestException("Simulated network failure")));
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_http_call_throws()
    {
        var subject = WithHandler(new ThrowingHttpMessageHandler(
            new HttpRequestException("Simulated network failure")));
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_http_call_throws()
    {
        var subject = WithHandler(new ThrowingHttpMessageHandler(
            new HttpRequestException("Simulated network failure")));
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_http_call_throws()
    {
        var subject = WithHandler(new ThrowingHttpMessageHandler(
            new HttpRequestException("Simulated network failure")));
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnHealthIssue("Test", "message"));
    }

    [Test]
    public void WebhookUrl_should_default_to_empty_string()
    {
        Assert.That(_subject.WebhookUrl, Is.EqualTo(""));
    }

    [Test]
    public void WebhookUrl_should_be_settable()
    {
        _subject.WebhookUrl = "https://hooks.example.com/mywebhook";

        Assert.That(_subject.WebhookUrl, Is.EqualTo("https://hooks.example.com/mywebhook"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_webhook_url_is_null()
    {
        _subject.WebhookUrl = null;

        Assert.DoesNotThrow(() => _subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_webhook_url_is_null()
    {
        _subject.WebhookUrl = null;

        Assert.DoesNotThrow(() => _subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_webhook_url_is_null()
    {
        _subject.WebhookUrl = null;

        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_webhook_url_is_whitespace()
    {
        _subject.WebhookUrl = "   ";

        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_webhook_url_is_whitespace()
    {
        _subject.WebhookUrl = "   ";

        Assert.DoesNotThrow(() => _subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_webhook_url_is_whitespace()
    {
        _subject.WebhookUrl = "   ";

        Assert.DoesNotThrow(() => _subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_webhook_url_is_private_ip()
    {
        _subject.WebhookUrl = "http://192.168.1.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_webhook_url_is_private_ip()
    {
        _subject.WebhookUrl = "http://192.168.1.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_webhook_url_is_private_ip()
    {
        _subject.WebhookUrl = "http://192.168.1.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void SendPayload_triggers_retry_on_server_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "Error");
        handler.Enqueue(HttpStatusCode.OK, "OK");

        var retryCount = 0;
        var policy = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
                OnRetry = _ =>
                {
                    retryCount++;
                    return ValueTask.CompletedTask;
                },
            })
            .Build();

        var subject = new WebhookNotification(new HttpClient(handler), policy)
        {
            WebhookUrl = "http://8.8.8.8/webhook",
        };

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("test.torrent"));
        Assert.That(retryCount, Is.EqualTo(1), "Retry should be triggered when 500 error is returned");
    }
}
