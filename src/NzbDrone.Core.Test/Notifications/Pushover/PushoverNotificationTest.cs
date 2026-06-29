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
}
