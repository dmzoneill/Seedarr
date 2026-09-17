using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DiskSpace;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Telegram;
using NzbDrone.Core.Test.TestHelpers;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Notifications.Telegram;

[TestFixture]
public class TelegramUpdateHandlerTest
{
    private ITorrentService _torrentService;
    private IConfigService _configService;
    private IDiskSpaceService _diskSpaceService;
    private MockHttpMessageHandler _httpHandler;
    private HttpClient _httpClient;
    private TelegramSettings _settings;
    private TelegramUpdateHandler _handler;

    [SetUp]
    public void SetUp()
    {
        _torrentService = Substitute.For<ITorrentService>();
        _configService = Substitute.For<IConfigService>();
        _diskSpaceService = Substitute.For<IDiskSpaceService>();
        _httpHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_httpHandler);

        _settings = new TelegramSettings
        {
            BotToken = "123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11",
            ChatId = "1001",
            AllowedChatIds = new List<long> { 1001, 1002 }
        };

        _handler = new TelegramUpdateHandler(
            _torrentService,
            _configService,
            _settings,
            _httpClient,
            _diskSpaceService);
    }

    [Test]
    public void IsAuthorized_should_return_true_when_chatId_is_in_AllowedChatIds()
    {
        Assert.That(_handler.IsAuthorized(1001), Is.True);
        Assert.That(_handler.IsAuthorized(1002), Is.True);
    }

    [Test]
    public void IsAuthorized_should_return_true_when_userId_is_in_AllowedChatIds()
    {
        Assert.That(_handler.IsAuthorized(9999, 1001), Is.True);
    }

    [Test]
    public void IsAuthorized_should_return_false_when_neither_chatId_nor_userId_are_in_AllowedChatIds()
    {
        Assert.That(_handler.IsAuthorized(9999), Is.False);
        Assert.That(_handler.IsAuthorized(9999, 8888), Is.False);
    }

    [Test]
    public void IsAuthorized_should_fallback_to_ChatId_when_AllowedChatIds_is_empty()
    {
        _settings.AllowedChatIds = new List<long>();
        _settings.ChatId = "5555";

        Assert.That(_handler.IsAuthorized(5555), Is.True);
        Assert.That(_handler.IsAuthorized(1001), Is.False);
    }

    [Test]
    public async Task HandleUpdateAsync_unauthorized_message_should_return_unauthorized_and_send_rejection()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var update = new TelegramUpdate
        {
            UpdateId = 1,
            Message = new TelegramMessage
            {
                MessageId = 10,
                Chat = new TelegramChat { Id = 9999 },
                Text = "/status"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        Assert.That(response.Authorized, Is.False);
        Assert.That(response.Success, Is.False);
        Assert.That(response.Handled, Is.True);
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(1));
        Assert.That(_httpHandler.LastRequest.RequestUri.ToString(), Does.Contain("sendMessage"));
    }

    [Test]
    public async Task HandleUpdateAsync_unauthorized_callback_query_should_answer_with_unauthorized()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var update = new TelegramUpdate
        {
            UpdateId = 2,
            CallbackQuery = new TelegramCallbackQuery
            {
                Id = "cb_1",
                From = new TelegramUser { Id = 9999 },
                Data = "pause:1"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        Assert.That(response.Authorized, Is.False);
        Assert.That(response.Success, Is.False);
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(1));
        Assert.That(_httpHandler.LastRequest.RequestUri.ToString(), Does.Contain("answerCallbackQuery"));
    }

    [Test]
    public async Task HandleUpdateAsync_start_command_should_return_help_text()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var update = new TelegramUpdate
        {
            UpdateId = 3,
            Message = new TelegramMessage
            {
                MessageId = 20,
                Chat = new TelegramChat { Id = 1001 },
                Text = "/start"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.Command, Is.EqualTo("/start"));
        Assert.That(response.ResponseText, Does.Contain("/status"));
        Assert.That(response.ResponseText, Does.Contain("/torrents"));
        Assert.That(response.ResponseText, Does.Contain("/pause"));
        Assert.That(response.ResponseText, Does.Contain("/resume"));
        Assert.That(response.ResponseText, Does.Contain("/turtle"));
    }

    [Test]
    public async Task HandleUpdateAsync_status_command_should_return_speeds_and_counts()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var torrents = new List<Torrent>
        {
            new Torrent { Id = 1, Name = "Ubuntu", Status = TorrentStatus.Downloading, DownloadSpeed = 1024 * 1024 * 2, UploadSpeed = 1024 * 50 },
            new Torrent { Id = 2, Name = "Debian", Status = TorrentStatus.Seeding, DownloadSpeed = 0, UploadSpeed = 1024 * 1024 },
            new Torrent { Id = 3, Name = "Arch", Status = TorrentStatus.Paused, DownloadSpeed = 0, UploadSpeed = 0 }
        };

        _torrentService.GetAll().Returns(torrents);
        _configService.AlternativeSpeedEnabled.Returns(true);

        var update = new TelegramUpdate
        {
            UpdateId = 4,
            Message = new TelegramMessage
            {
                MessageId = 21,
                Chat = new TelegramChat { Id = 1001 },
                Text = "/status"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.Command, Is.EqualTo("/status"));
        Assert.That(response.ResponseText, Does.Contain("Seedarr Status"));
        Assert.That(response.ResponseText, Does.Contain("Turtle Mode"));
        Assert.That(response.ResponseText, Does.Contain("Active"));
        Assert.That(response.ResponseText, Does.Contain("3 total"));
        Assert.That(response.ResponseText, Does.Contain("1 dl"));
        Assert.That(response.ResponseText, Does.Contain("1 seed"));
        Assert.That(response.ResponseText, Does.Contain("1 paused"));
    }

    [Test]
    public async Task HandleUpdateAsync_torrents_command_should_format_torrent_list()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var torrents = new List<Torrent>
        {
            new Torrent
            {
                Id = 42,
                Name = "Linux Mint",
                Status = TorrentStatus.Downloading,
                Progress = 0.75,
                DownloadSpeed = 1024 * 1024 * 5,
                UploadSpeed = 1024 * 256,
                Ratio = 1.25
            }
        };

        _torrentService.GetAll().Returns(torrents);

        var update = new TelegramUpdate
        {
            UpdateId = 5,
            Message = new TelegramMessage
            {
                MessageId = 22,
                Chat = new TelegramChat { Id = 1001 },
                Text = "/torrents"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.Command, Is.EqualTo("/torrents"));
        Assert.That(response.ResponseText, Does.Contain("Linux Mint"));
        Assert.That(response.ResponseText, Does.Contain("75.0%"));
        Assert.That(response.ResponseText, Does.Contain("Ratio: 1.25"));
    }

    [Test]
    public async Task HandleUpdateAsync_torrents_command_when_empty_should_notify_no_torrents()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");
        _torrentService.GetAll().Returns(new List<Torrent>());

        var update = new TelegramUpdate
        {
            UpdateId = 6,
            Message = new TelegramMessage
            {
                MessageId = 23,
                Chat = new TelegramChat { Id = 1001 },
                Text = "/torrents"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        Assert.That(response.Authorized, Is.True);
        Assert.That(response.ResponseText, Does.Contain("No torrents found"));
    }

    [Test]
    public async Task HandleUpdateAsync_pause_command_should_pause_existing_torrent()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var torrent = new Torrent { Id = 7, Name = "Fedora" };
        _torrentService.Get(7).Returns(torrent);

        var update = new TelegramUpdate
        {
            UpdateId = 7,
            Message = new TelegramMessage
            {
                MessageId = 24,
                Chat = new TelegramChat { Id = 1001 },
                Text = "/pause 7"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        _torrentService.Received(1).Pause(7);
        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.ResponseText, Does.Contain("Paused torrent: <b>Fedora</b>"));
    }

    [Test]
    public async Task HandleUpdateAsync_pause_command_with_invalid_id_should_return_usage()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var update = new TelegramUpdate
        {
            UpdateId = 8,
            Message = new TelegramMessage
            {
                MessageId = 25,
                Chat = new TelegramChat { Id = 1001 },
                Text = "/pause invalid"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        _torrentService.DidNotReceive().Pause(Arg.Any<int>());
        Assert.That(response.Success, Is.False);
        Assert.That(response.ResponseText, Does.Contain("Usage: /pause"));
    }

    [Test]
    public async Task HandleUpdateAsync_resume_command_should_start_existing_torrent()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var torrent = new Torrent { Id = 8, Name = "Alpine Linux" };
        _torrentService.Get(8).Returns(torrent);

        var update = new TelegramUpdate
        {
            UpdateId = 9,
            Message = new TelegramMessage
            {
                MessageId = 26,
                Chat = new TelegramChat { Id = 1001 },
                Text = "/resume 8"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        _torrentService.Received(1).Start(8);
        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.ResponseText, Does.Contain("Resumed torrent: <b>Alpine Linux</b>"));
    }

    [Test]
    public async Task HandleUpdateAsync_turtle_command_should_toggle_alternate_speed()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");
        _configService.AlternativeSpeedEnabled.Returns(false);

        var update = new TelegramUpdate
        {
            UpdateId = 10,
            Message = new TelegramMessage
            {
                MessageId = 27,
                Chat = new TelegramChat { Id = 1001 },
                Text = "/turtle"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (bool)d["AlternativeSpeedEnabled"] == true));
        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.ResponseText, Does.Contain("Turtle mode enabled"));
    }

    [Test]
    public async Task HandleUpdateAsync_callback_query_pause_should_pause_and_answer()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}"); // answerCallbackQuery
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}"); // editMessageReplyMarkup

        var update = new TelegramUpdate
        {
            UpdateId = 11,
            CallbackQuery = new TelegramCallbackQuery
            {
                Id = "cb_pause_1",
                From = new TelegramUser { Id = 1001 },
                Message = new TelegramMessage
                {
                    MessageId = 55,
                    Chat = new TelegramChat { Id = 1001 }
                },
                Data = "pause:12"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        _torrentService.Received(1).Pause(12);
        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.CallbackQueryAnswer, Does.Contain("Paused torrent #12"));
        Assert.That(_httpHandler.Requests.Any(r => r.RequestUri.ToString().Contains("answerCallbackQuery")), Is.True);
        Assert.That(_httpHandler.Requests.Any(r => r.RequestUri.ToString().Contains("editMessageReplyMarkup")), Is.True);
    }

    [Test]
    public async Task HandleUpdateAsync_callback_query_resume_should_resume_and_answer()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}"); // answerCallbackQuery
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}"); // editMessageReplyMarkup

        var update = new TelegramUpdate
        {
            UpdateId = 12,
            CallbackQuery = new TelegramCallbackQuery
            {
                Id = "cb_resume_1",
                From = new TelegramUser { Id = 1001 },
                Message = new TelegramMessage
                {
                    MessageId = 56,
                    Chat = new TelegramChat { Id = 1001 }
                },
                Data = "resume:12"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        _torrentService.Received(1).Start(12);
        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.CallbackQueryAnswer, Does.Contain("Resumed torrent #12"));
        Assert.That(_httpHandler.Requests.Any(r => r.RequestUri.ToString().Contains("answerCallbackQuery")), Is.True);
        Assert.That(_httpHandler.Requests.Any(r => r.RequestUri.ToString().Contains("editMessageReplyMarkup")), Is.True);
    }

    [Test]
    public async Task HandleUpdateAsync_callback_query_turtle_toggle_should_toggle_and_answer()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}"); // answerCallbackQuery
        _configService.AlternativeSpeedEnabled.Returns(false);

        var update = new TelegramUpdate
        {
            UpdateId = 13,
            CallbackQuery = new TelegramCallbackQuery
            {
                Id = "cb_turtle_1",
                From = new TelegramUser { Id = 1001 },
                Data = "turtle:toggle"
            }
        };

        var response = await _handler.HandleUpdateAsync(update);

        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d => (bool)d["AlternativeSpeedEnabled"] == true));
        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.CallbackQueryAnswer, Does.Contain("Turtle mode ON"));
    }

    [Test]
    public async Task HandleUpdateAsync_json_string_should_deserialize_and_route()
    {
        _httpHandler.Enqueue(HttpStatusCode.OK, "{}");

        var json = "{\"update_id\":14,\"message\":{\"message_id\":80,\"chat\":{\"id\":1001},\"text\":\"/start\"}}";
        var response = await _handler.HandleUpdateAsync(json);

        Assert.That(response.Authorized, Is.True);
        Assert.That(response.Success, Is.True);
        Assert.That(response.Command, Is.EqualTo("/start"));
    }

    [Test]
    public async Task HandleUpdateAsync_invalid_json_should_return_unhandled_without_throwing()
    {
        var response = await _handler.HandleUpdateAsync("{invalid_json");

        Assert.That(response.Success, Is.False);
        Assert.That(response.Handled, Is.False);
    }

    [Test]
    public void FormatTelegramHtml_should_escape_special_characters()
    {
        var raw = "Torrent <tag> & [brackets] > other";
        var formatted = TelegramUpdateHandler.FormatTelegramHtml(raw);

        Assert.That(formatted, Does.Contain("&lt;tag&gt;"));
        Assert.That(formatted, Does.Contain("&amp;"));
        Assert.That(formatted, Does.Contain("&gt;"));
        Assert.That(formatted, Does.Not.Contain("<tag>"));
    }

    [Test]
    public void NotificationPayloadBuilder_should_include_reply_markup_with_inline_keyboard()
    {
        var torrent = new Torrent
        {
            Id = 99,
            Name = "Big Movie",
            Status = TorrentStatus.Downloading,
            Progress = 0.5
        };

        var payload = NotificationPayloadBuilder.BuildProviderPayload(
            "Telegram",
            "Grab",
            torrent,
            null,
            null,
            "{\"chatId\":\"1001\"}") as Dictionary<string, object>;

        Assert.That(payload, Is.Not.Null);
        Assert.That(payload.ContainsKey("reply_markup"), Is.True);

        var markupJson = JsonSerializer.Serialize(payload["reply_markup"]);
        Assert.That(markupJson, Does.Contain("pause:99"));
        Assert.That(markupJson, Does.Contain("turtle:toggle"));
    }
}
