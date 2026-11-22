using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Telegram;

namespace NzbDrone.Core.Test.Notifications.Telegram;

[TestFixture]
public class TelegramNotificationTest
{
    [Test]
    public void Lifecycle_methods_skip_send_when_bot_is_not_configured()
    {
        var subject = new TelegramNotification();

        Assert.DoesNotThrow(() => subject.OnTorrentAdded("demo.torrent"));
        Assert.DoesNotThrow(() => subject.OnSeedingStarted("demo.torrent"));
        Assert.DoesNotThrow(() => subject.OnSeedingStopped("demo.torrent"));
        Assert.DoesNotThrow(() => subject.OnHealthIssue("Disk", "Low space"));
    }

    [Test]
    public void OnTorrentAdded_posts_to_telegram_when_configured()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var subject = new TelegramNotification(client)
        {
            Settings = new TelegramSettings { BotToken = "123:abc", ChatId = "42" }
        };

        subject.OnTorrentAdded("demo.torrent");

        Assert.That(handler.Calls, Is.EqualTo(1));
        Assert.That(handler.LastUrl, Does.Contain("/bot123:abc/sendMessage"));
    }

    [Test]
    public void OnHealthIssue_swallows_http_failures()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError, throwOnSend: true);
        using var client = new HttpClient(handler);
        var subject = new TelegramNotification(client)
        {
            Settings = new TelegramSettings { BotToken = "123:abc", ChatId = "42" }
        };

        Assert.DoesNotThrow(() => subject.OnHealthIssue("Disk", "Low space"));
        Assert.That(handler.Calls, Is.EqualTo(1));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly bool _throwOnSend;

        public RecordingHandler(HttpStatusCode status, bool throwOnSend = false)
        {
            _status = status;
            _throwOnSend = throwOnSend;
        }

        public int Calls { get; private set; }

        public string LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri?.ToString();
            if (_throwOnSend)
            {
                throw new HttpRequestException("telegram down");
            }

            return Task.FromResult(new HttpResponseMessage(_status));
        }
    }
}
