using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Notifications.Telegram;
using Seedarr.Api.V1.Telegram;

namespace Seedarr.Api.V1.Test.Telegram;

[TestFixture]
public class TelegramBotControllerTest
{
    private IConfigService _configService;
    private ITelegramUpdateHandler _updateHandler;
    private TelegramBotController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.TelegramSecretToken.Returns("valid-secret-token-123");

        _updateHandler = Substitute.For<ITelegramUpdateHandler>();

        _controller = new TelegramBotController(
            _configService,
            _updateHandler);
    }

    private void SetSecretTokenHeader(string token)
    {
        var httpContext = new DefaultHttpContext();
        if (token != null)
        {
            httpContext.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = token;
        }

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Test]
    public async Task ReceiveWebhook_should_return_403_when_secret_token_header_is_missing()
    {
        SetSecretTokenHeader(null);

        var update = new TelegramUpdate { UpdateId = 1 };
        var result = await _controller.ReceiveWebhook(update);

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusCodeResult = (StatusCodeResult)result;
        Assert.That(statusCodeResult.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        await _updateHandler.DidNotReceive().HandleUpdateAsync(Arg.Any<TelegramUpdate>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveWebhook_should_return_403_when_secret_token_header_is_incorrect()
    {
        SetSecretTokenHeader("wrong-secret-token");

        var update = new TelegramUpdate { UpdateId = 1 };
        var result = await _controller.ReceiveWebhook(update);

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusCodeResult = (StatusCodeResult)result;
        Assert.That(statusCodeResult.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        await _updateHandler.DidNotReceive().HandleUpdateAsync(Arg.Any<TelegramUpdate>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveWebhook_should_return_403_when_configured_secret_token_is_empty()
    {
        _configService.TelegramSecretToken.Returns(string.Empty);
        SetSecretTokenHeader("valid-secret-token-123");

        var update = new TelegramUpdate { UpdateId = 1 };
        var result = await _controller.ReceiveWebhook(update);

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusCodeResult = (StatusCodeResult)result;
        Assert.That(statusCodeResult.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
        await _updateHandler.DidNotReceive().HandleUpdateAsync(Arg.Any<TelegramUpdate>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveWebhook_should_return_200_and_forward_update_when_secret_token_is_valid()
    {
        SetSecretTokenHeader("valid-secret-token-123");

        var update = new TelegramUpdate
        {
            UpdateId = 42,
            Message = new TelegramMessage { MessageId = 1, Text = "/status" }
        };

        var expectedResponse = new TelegramResponse
        {
            Success = true,
            Handled = true,
            Authorized = true,
            ResponseText = "Status OK"
        };

        _updateHandler.HandleUpdateAsync(update, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResponse));

        var result = await _controller.ReceiveWebhook(update);

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        Assert.That(okResult.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(okResult.Value, Is.EqualTo(expectedResponse));

        await _updateHandler.Received(1).HandleUpdateAsync(update, Arg.Any<CancellationToken>());
    }
}
