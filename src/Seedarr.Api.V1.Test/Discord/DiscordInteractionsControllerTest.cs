using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Notifications.Discord;
using Seedarr.Api.V1.Discord;

namespace Seedarr.Api.V1.Test.Discord;

[TestFixture]
public class DiscordInteractionsControllerTest
{
    private IConfigService _configService;
    private IDiscordSecurityService _securityService;
    private IDiscordInteractionHandler _interactionHandler;
    private DiscordInteractionsController _controller;

    private const string ValidPublicKey = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
    private const string ValidSignature = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
    private const string ValidTimestamp = "1620000000";

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.DiscordPublicKey.Returns(ValidPublicKey);

        _securityService = Substitute.For<IDiscordSecurityService>();
        _interactionHandler = Substitute.For<IDiscordInteractionHandler>();

        _controller = new DiscordInteractionsController(
            _configService,
            _securityService,
            _interactionHandler);
    }

    private void SetupRequest(string signature, string timestamp, string bodyContent = "{}")
    {
        var httpContext = new DefaultHttpContext();

        if (signature != null)
        {
            httpContext.Request.Headers[DiscordInteractionsController.SignatureHeader] = signature;
        }

        if (timestamp != null)
        {
            httpContext.Request.Headers[DiscordInteractionsController.TimestampHeader] = timestamp;
        }

        var bodyBytes = Encoding.UTF8.GetBytes(bodyContent);
        httpContext.Request.Body = new MemoryStream(bodyBytes);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    [Test]
    public async Task ReceiveInteraction_should_return_401_when_signature_header_is_missing()
    {
        SetupRequest(null, ValidTimestamp, "{\"type\":1}");

        var result = await _controller.ReceiveInteraction();

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
        await _interactionHandler.DidNotReceive().HandleInteractionAsync(Arg.Any<DiscordInteraction>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveInteraction_should_return_401_when_timestamp_header_is_missing()
    {
        SetupRequest(ValidSignature, null, "{\"type\":1}");

        var result = await _controller.ReceiveInteraction();

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
        await _interactionHandler.DidNotReceive().HandleInteractionAsync(Arg.Any<DiscordInteraction>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveInteraction_should_return_401_when_configured_public_key_is_empty()
    {
        _configService.DiscordPublicKey.Returns(string.Empty);
        SetupRequest(ValidSignature, ValidTimestamp, "{\"type\":1}");

        var result = await _controller.ReceiveInteraction();

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
        await _interactionHandler.DidNotReceive().HandleInteractionAsync(Arg.Any<DiscordInteraction>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveInteraction_should_return_401_when_signature_is_invalid()
    {
        SetupRequest(ValidSignature, ValidTimestamp, "{\"type\":1}");

        _securityService.VerifySignature(ValidSignature, ValidTimestamp, Arg.Any<byte[]>(), ValidPublicKey)
            .Returns(false);

        var result = await _controller.ReceiveInteraction();

        Assert.That(result, Is.InstanceOf<UnauthorizedObjectResult>());
        var unauthorized = (UnauthorizedObjectResult)result;
        Assert.That(unauthorized.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        Assert.That(unauthorized.Value, Is.EqualTo("Invalid request signature"));

        await _interactionHandler.DidNotReceive().HandleInteractionAsync(Arg.Any<DiscordInteraction>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveInteraction_should_return_200_ok_with_pong_for_type_1_ping()
    {
        SetupRequest(ValidSignature, ValidTimestamp, "{\"type\":1}");

        _securityService.VerifySignature(ValidSignature, ValidTimestamp, Arg.Any<byte[]>(), ValidPublicKey)
            .Returns(true);

        var result = await _controller.ReceiveInteraction();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        Assert.That(okResult.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(okResult.Value, Is.InstanceOf<DiscordInteractionResponse>());

        var response = (DiscordInteractionResponse)okResult.Value;
        Assert.That(response.Type, Is.EqualTo(DiscordInteractionResponseType.Pong));

        await _interactionHandler.DidNotReceive().HandleInteractionAsync(Arg.Any<DiscordInteraction>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveInteraction_should_forward_application_command_to_handler()
    {
        var json = "{\"type\":2,\"data\":{\"name\":\"status\"}}";
        SetupRequest(ValidSignature, ValidTimestamp, json);

        _securityService.VerifySignature(ValidSignature, ValidTimestamp, Arg.Any<byte[]>(), ValidPublicKey)
            .Returns(true);

        var expectedResponse = new DiscordInteractionResponse
        {
            Type = DiscordInteractionResponseType.ChannelMessageWithSource,
            Success = true,
        };

        _interactionHandler.HandleInteractionAsync(Arg.Is<DiscordInteraction>(i => i.Type == 2 && i.Data.Name == "status"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResponse));

        var result = await _controller.ReceiveInteraction();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        Assert.That(okResult.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(okResult.Value, Is.EqualTo(expectedResponse));

        await _interactionHandler.Received(1).HandleInteractionAsync(Arg.Is<DiscordInteraction>(i => i.Type == 2 && i.Data.Name == "status"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReceiveInteraction_should_forward_message_component_to_handler()
    {
        var json = "{\"type\":3,\"data\":{\"custom_id\":\"pause:15\"}}";
        SetupRequest(ValidSignature, ValidTimestamp, json);

        _securityService.VerifySignature(ValidSignature, ValidTimestamp, Arg.Any<byte[]>(), ValidPublicKey)
            .Returns(true);

        var expectedResponse = new DiscordInteractionResponse
        {
            Type = DiscordInteractionResponseType.UpdateMessage,
            Success = true,
        };

        _interactionHandler.HandleInteractionAsync(Arg.Is<DiscordInteraction>(i => i.Type == 3 && i.Data.CustomId == "pause:15"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResponse));

        var result = await _controller.ReceiveInteraction();

        Assert.That(result, Is.InstanceOf<OkObjectResult>());
        var okResult = (OkObjectResult)result;
        Assert.That(okResult.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(okResult.Value, Is.EqualTo(expectedResponse));

        await _interactionHandler.Received(1).HandleInteractionAsync(Arg.Is<DiscordInteraction>(i => i.Type == 3 && i.Data.CustomId == "pause:15"), Arg.Any<CancellationToken>());
    }
}
