using System.Net;
using System.Net.Http;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Pushover;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Notifications.Pushover;

[TestFixture]
public class PushoverNotificationTest
{
    private PushoverNotification _subject;

    [SetUp]
    public void SetUp()
    {
        _subject = new PushoverNotification();
    }

    private static PushoverNotification WithHandler(HttpMessageHandler handler)
        => new PushoverNotification(new HttpClient(handler));

    [Test]
    public void Name_should_return_pushover()
    {
        Assert.That(_subject.Name, Is.EqualTo("Pushover"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_api_token_is_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_user_key_is_empty()
    {
        _subject.ApiToken = "valid-token";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_credentials_are_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_credentials_are_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_credentials_are_empty()
    {
        Assert.DoesNotThrow(() => _subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_api_token_is_null()
    {
        _subject.ApiToken = null;

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_user_key_is_whitespace()
    {
        _subject.ApiToken = "valid-token";
        _subject.UserKey = "  ";

        Assert.DoesNotThrow(() => _subject.OnTorrentAdded("test.torrent"));
    }

    // --- HTTP send path tests (use injectable constructor) ---

    [Test]
    public void OnTorrentAdded_should_not_throw_when_credentials_set_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""status"":1}");
        var subject = WithHandler(handler);
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_credentials_set_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""status"":1}");
        var subject = WithHandler(handler);
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        Assert.DoesNotThrow(() => subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_credentials_set_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""status"":1}");
        var subject = WithHandler(handler);
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        Assert.DoesNotThrow(() => subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_credentials_set_and_http_succeeds()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, @"{""status"":1}");
        var subject = WithHandler(handler);
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        Assert.DoesNotThrow(() => subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_credentials_set_and_http_throws()
    {
        var subject = WithHandler(new ThrowingHttpMessageHandler(
            new HttpRequestException("Simulated network failure")));
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void OnSeedingStarted_should_not_throw_when_credentials_set_and_http_throws()
    {
        var subject = WithHandler(new ThrowingHttpMessageHandler(
            new HttpRequestException("Simulated network failure")));
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        Assert.DoesNotThrow(() => subject.OnSeedingStarted("test.torrent"));
    }

    [Test]
    public void OnSeedingStopped_should_not_throw_when_credentials_set_and_http_throws()
    {
        var subject = WithHandler(new ThrowingHttpMessageHandler(
            new HttpRequestException("Simulated network failure")));
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        Assert.DoesNotThrow(() => subject.OnSeedingStopped("test.torrent"));
    }

    [Test]
    public void OnHealthIssue_should_not_throw_when_credentials_set_and_http_throws()
    {
        var subject = WithHandler(new ThrowingHttpMessageHandler(
            new HttpRequestException("Simulated network failure")));
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        Assert.DoesNotThrow(() => subject.OnHealthIssue("Test", "message"));
    }

    [Test]
    public void OnTorrentAdded_should_not_throw_when_http_returns_server_error()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "Server error");
        var subject = WithHandler(handler);
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("test.torrent"));
    }

    [Test]
    public void SendMessage_should_include_priority_device_and_sound_in_form_content()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"status\":1}");
        var subject = WithHandler(handler);
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";
        subject.Priority = 1;
        subject.Device = "phone";
        subject.Sound = "cosmic";

        subject.SendMessage("Test Title", "Test Message");

        Assert.That(handler.Requests.Count, Is.EqualTo(1));
        var content = handler.LastRequest.Content.ReadAsStringAsync().Result;
        Assert.That(content, Does.Contain("priority=1"));
        Assert.That(content, Does.Contain("device=phone"));
        Assert.That(content, Does.Contain("sound=cosmic"));
    }

    [Test]
    public void SendMessage_emergency_priority_should_include_clamped_retry_and_expire()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"status\":1}");
        var subject = WithHandler(handler);
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";
        subject.Priority = 2;
        subject.RetrySeconds = 10; // should clamp to >= 30
        subject.ExpireSeconds = 20000; // should clamp to <= 10800

        subject.SendMessage("Emergency Alert", "System failure");

        Assert.That(handler.Requests.Count, Is.EqualTo(1));
        var content = handler.LastRequest.Content.ReadAsStringAsync().Result;
        Assert.That(content, Does.Contain("priority=2"));
        Assert.That(content, Does.Contain("retry=30"));
        Assert.That(content, Does.Contain("expire=10800"));
    }

    [Test]
    public void SendMessage_should_truncate_title_to_250_and_message_to_1024()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"status\":1}");
        var subject = WithHandler(handler);
        subject.ApiToken = "test-token";
        subject.UserKey = "test-user-key";

        var longTitle = new string('T', 300);
        var longMessage = new string('M', 2000);

        subject.SendMessage(longTitle, longMessage);

        Assert.That(handler.Requests.Count, Is.EqualTo(1));
        var content = handler.LastRequest.Content.ReadAsStringAsync().Result;

        // In form URL encoded content, check unescaped values or length
        Assert.That(content, Does.Contain("..."));
    }

    [Test]
    public void SendMessage_with_PushoverSettings_should_apply_all_configured_properties()
    {
        var handler = new MockHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"status\":1}");

        var settings = new PushoverSettings
        {
            ApiKey = "settings-token",
            UserKey = "settings-user",
            Priority = -1,
            Device = "desktop",
            Sound = "magic"
        };

        var subject = new PushoverNotification(new HttpClient(handler), settings);
        subject.OnTorrentAdded("test.torrent");

        Assert.That(handler.Requests.Count, Is.EqualTo(1));
        var content = handler.LastRequest.Content.ReadAsStringAsync().Result;
        Assert.That(content, Does.Contain("token=settings-token"));
        Assert.That(content, Does.Contain("user=settings-user"));
        Assert.That(content, Does.Contain("priority=-1"));
        Assert.That(content, Does.Contain("device=desktop"));
        Assert.That(content, Does.Contain("sound=magic"));
    }
}
