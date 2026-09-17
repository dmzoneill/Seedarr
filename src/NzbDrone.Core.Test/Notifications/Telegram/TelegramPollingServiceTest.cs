using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Notifications.Telegram;
using NzbDrone.Core.Test.TestHelpers;

namespace NzbDrone.Core.Test.Notifications.Telegram;

[TestFixture]
public class TelegramPollingServiceTest
{
    private IConfigService _configService;
    private ITelegramUpdateHandler _updateHandler;
    private MockHttpMessageHandler _httpHandler;
    private HttpClient _httpClient;
    private TelegramPollingService _service;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.TelegramBotToken.Returns("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11");
        _configService.TelegramUsePolling.Returns(true);

        _updateHandler = Substitute.For<ITelegramUpdateHandler>();
        _httpHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_httpHandler);

        _service = new TelegramPollingService(
            _configService,
            _updateHandler,
            _httpClient);
    }

    [Test]
    public async Task PollOnceAsync_should_return_0_when_bot_token_is_empty()
    {
        _configService.TelegramBotToken.Returns(string.Empty);

        var count = await _service.PollOnceAsync();

        Assert.That(count, Is.EqualTo(0));
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(0));
    }

    [Test]
    public async Task PollOnceAsync_should_fetch_updates_and_increment_offset()
    {
        var apiResponse = new TelegramApiResponse<List<TelegramUpdate>>
        {
            Ok = true,
            Result = new List<TelegramUpdate>
            {
                new TelegramUpdate
                {
                    UpdateId = 100,
                    Message = new TelegramMessage { MessageId = 1, Text = "hello" }
                },
                new TelegramUpdate
                {
                    UpdateId = 101,
                    Message = new TelegramMessage { MessageId = 2, Text = "world" }
                }
            }
        };

        _httpHandler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(apiResponse));

        var processed = await _service.PollOnceAsync();

        Assert.That(processed, Is.EqualTo(2));
        Assert.That(_service.CurrentOffset, Is.EqualTo(102));
        await _updateHandler.Received(1).HandleUpdateAsync(Arg.Is<TelegramUpdate>(u => u.UpdateId == 100), Arg.Any<CancellationToken>());
        await _updateHandler.Received(1).HandleUpdateAsync(Arg.Is<TelegramUpdate>(u => u.UpdateId == 101), Arg.Any<CancellationToken>());

        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(1));
        Assert.That(_httpHandler.LastRequest.RequestUri.ToString(), Does.Contain("offset=0"));
    }

    [Test]
    public async Task PollOnceAsync_should_not_reprocess_duplicate_updates()
    {
        _service.CurrentOffset = 105;

        var apiResponse = new TelegramApiResponse<List<TelegramUpdate>>
        {
            Ok = true,
            Result = new List<TelegramUpdate>
            {
                new TelegramUpdate
                {
                    UpdateId = 104,
                    Message = new TelegramMessage { MessageId = 1, Text = "duplicate" }
                },
                new TelegramUpdate
                {
                    UpdateId = 105,
                    Message = new TelegramMessage { MessageId = 2, Text = "new update" }
                }
            }
        };

        _httpHandler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(apiResponse));

        var processed = await _service.PollOnceAsync();

        Assert.That(processed, Is.EqualTo(1));
        Assert.That(_service.CurrentOffset, Is.EqualTo(106));
        await _updateHandler.DidNotReceive().HandleUpdateAsync(Arg.Is<TelegramUpdate>(u => u.UpdateId == 104), Arg.Any<CancellationToken>());
        await _updateHandler.Received(1).HandleUpdateAsync(Arg.Is<TelegramUpdate>(u => u.UpdateId == 105), Arg.Any<CancellationToken>());
    }

    [Test]
    public void PollOnceAsync_should_throw_on_http_429()
    {
        _httpHandler.Enqueue((HttpStatusCode)429, "Too Many Requests");

        Assert.ThrowsAsync<HttpRequestException>(async () => await _service.PollOnceAsync());
    }

    [Test]
    public async Task PollOnceAsync_should_return_0_when_no_updates()
    {
        var apiResponse = new TelegramApiResponse<List<TelegramUpdate>>
        {
            Ok = true,
            Result = new List<TelegramUpdate>()
        };

        _httpHandler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(apiResponse));

        var processed = await _service.PollOnceAsync();

        Assert.That(processed, Is.EqualTo(0));
        Assert.That(_service.CurrentOffset, Is.EqualTo(0));
    }
}
