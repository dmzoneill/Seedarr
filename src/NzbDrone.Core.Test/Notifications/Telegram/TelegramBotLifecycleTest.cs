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
public class TelegramBotLifecycleTest
{
    private IConfigService _configService;
    private MockHttpMessageHandler _httpHandler;
    private HttpClient _httpClient;
    private TelegramBotLifecycle _lifecycle;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.TelegramBotToken.Returns("123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11");
        _configService.TelegramWebhookUrl.Returns("https://example.com/api/v1/telegram/webhook");
        _configService.TelegramSecretToken.Returns("secret-123");

        _httpHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_httpHandler);

        _lifecycle = new TelegramBotLifecycle(_configService, _httpClient);
    }

    [Test]
    public async Task SetWebhookAsync_should_post_to_telegram_api_and_return_true()
    {
        var apiResponse = new TelegramApiResponse<bool>
        {
            Ok = true,
            Result = true
        };

        _httpHandler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(apiResponse));

        var success = await _lifecycle.SetWebhookAsync();

        Assert.That(success, Is.True);
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(1));
        var request = _httpHandler.LastRequest;
        Assert.That(request.RequestUri.ToString(), Does.Contain("/setWebhook"));

        var body = await request.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("https://example.com/api/v1/telegram/webhook"));
        Assert.That(body, Does.Contain("secret-123"));
    }

    [Test]
    public async Task DeleteWebhookAsync_should_post_to_telegram_api_and_return_true()
    {
        var apiResponse = new TelegramApiResponse<bool>
        {
            Ok = true,
            Result = true
        };

        _httpHandler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(apiResponse));

        var success = await _lifecycle.DeleteWebhookAsync(true);

        Assert.That(success, Is.True);
        Assert.That(_httpHandler.Requests.Count, Is.EqualTo(1));
        var request = _httpHandler.LastRequest;
        Assert.That(request.RequestUri.ToString(), Does.Contain("/deleteWebhook"));

        var body = await request.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("true"));
    }

    [Test]
    public async Task GetWebhookInfoAsync_should_return_parsed_info()
    {
        var apiResponse = new TelegramApiResponse<TelegramWebhookInfo>
        {
            Ok = true,
            Result = new TelegramWebhookInfo
            {
                Url = "https://example.com/api/v1/telegram/webhook",
                PendingUpdateCount = 0
            }
        };

        _httpHandler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(apiResponse));

        var info = await _lifecycle.GetWebhookInfoAsync();

        Assert.That(info, Is.Not.Null);
        Assert.That(info.Url, Is.EqualTo("https://example.com/api/v1/telegram/webhook"));
        Assert.That(info.PendingUpdateCount, Is.EqualTo(0));
    }
}
