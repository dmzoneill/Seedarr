using System.Net;
using System.Net.Http;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Discord;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Notifications.Discord;

[TestFixture]
public class DiscordNotificationTest
{
    private DiscordNotification _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new DiscordNotification();
    }

    private static DiscordNotification WithHandler(HttpMessageHandler handler)
        => new DiscordNotification(new HttpClient(handler));

    [Test]
    public void Name_should_return_discord()
    {
        Assert.That(_subject.Name, Is.EqualTo("Discord"));
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

    [Test]
    public void WebhookUrl_should_default_to_empty_string()
    {
        Assert.That(_subject.WebhookUrl, Is.EqualTo(""));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_webhook_url_is_null()
    {
        _subject.WebhookUrl = null;

        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("test.torrent"));
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
    public void OnSeedingStarted_should_not_throw_when_webhook_url_is_loopback()
    {
        _subject.WebhookUrl = "http://127.0.0.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_webhook_url_is_loopback()
    {
        _subject.WebhookUrl = "http://127.0.0.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_webhook_url_is_loopback()
    {
        _subject.WebhookUrl = "http://127.0.0.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_webhook_url_is_10_network()
    {
        _subject.WebhookUrl = "http://10.0.0.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_webhook_url_is_172_network()
    {
        _subject.WebhookUrl = "http://172.16.0.1/webhook";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_webhook_url_is_invalid()
    {
        _subject.WebhookUrl = "not-a-url";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_with_unreachable_public_url()
    {
        _subject.WebhookUrl = "http://nonexistent.invalid:9999/webhook";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_with_unreachable_public_url()
    {
        _subject.WebhookUrl = "http://nonexistent.invalid:9999/webhook";

        Assert.DoesNotThrow(() => _subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_with_unreachable_public_url()
    {
        _subject.WebhookUrl = "http://nonexistent.invalid:9999/webhook";

        Assert.DoesNotThrow(() => _subject.OnHealthIssue("Test", "message"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_with_ftp_scheme_url()
    {
        _subject.WebhookUrl = "ftp://example.com/webhook";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void WebhookUrl_should_be_settable()
    {
        _subject.WebhookUrl = "http://example.com/webhook";

        Assert.That(_subject.WebhookUrl, Is.EqualTo("http://example.com/webhook"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_with_unreachable_public_url()
    {
        _subject.WebhookUrl = "http://nonexistent.invalid:9999/webhook";

        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_torrent_name_is_null()
    {
        // null name passes through to embed description; empty WebhookUrl short-circuits safely
        Assert.DoesNotThrow(() => _subject.OnTorrentAdded(null));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_source_is_null()
    {
        Assert.DoesNotThrow(() => _subject.OnHealthIssue(null, "test message"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_message_is_null()
    {
        Assert.DoesNotThrow(() => _subject.OnHealthIssue("disk", null));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_torrent_name_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnSeedingStarted(""));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_torrent_name_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnSeedingStopped(""));
    }

    [Test]
    public void All_notification_methods_use_shared_webhook_url_setting()
    {
        // Verify setting WebhookUrl affects all notification methods.
        // With an empty URL, all should silently do nothing (guard path).
        _subject.WebhookUrl = "";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("torrent"));
        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("torrent"));
        Assert.DoesNotThrow(() => _subject.OnSeedingStopped("torrent"));
        Assert.DoesNotThrow(() => _subject.OnHealthIssue("source", "msg"));
    }

    // --- HTTP send path tests (use injectable constructor) ---

    [Test]
    public void OnTorrentAdded_should_not_throw_when_url_is_valid_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NoContent, "");
        var subject = WithHandler(handler);
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_url_is_valid_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NoContent, "");
        var subject = WithHandler(handler);
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_url_is_valid_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NoContent, "");
        var subject = WithHandler(handler);
        subject.WebhookUrl = "http://8.8.8.8/webhook";

        Assert.DoesNotThrow(() => subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_url_is_valid_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.NoContent, "");
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
}
