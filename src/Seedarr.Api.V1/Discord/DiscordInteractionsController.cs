using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Notifications.Discord;
using Seedarr.Http;

namespace Seedarr.Api.V1.Discord;

[V1ApiController("discord")]
public class DiscordInteractionsController : Controller
{
    public const string SignatureHeader = "X-Signature-Ed25519";
    public const string TimestampHeader = "X-Signature-Timestamp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IConfigService _configService;
    private readonly IDiscordSecurityService _securityService;
    private readonly IDiscordInteractionHandler _interactionHandler;
    private readonly Logger _logger;

    public DiscordInteractionsController(
        IConfigService configService,
        IDiscordSecurityService securityService,
        IDiscordInteractionHandler interactionHandler)
    {
        _configService = configService;
        _securityService = securityService;
        _interactionHandler = interactionHandler;
        _logger = LogManager.GetCurrentClassLogger();
    }

    [HttpPost("interactions")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveInteraction([FromBody] DiscordInteraction interaction = null, CancellationToken cancellationToken = default)
    {
        var signature = Request?.Headers?[SignatureHeader].FirstOrDefault();
        var timestamp = Request?.Headers?[TimestampHeader].FirstOrDefault();
        var publicKey = _configService?.DiscordPublicKey;

        if (string.IsNullOrWhiteSpace(signature) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(publicKey))
        {
            _logger.Warn("Discord interaction rejected: missing signature, timestamp, or public key");
            return Unauthorized("Invalid request signature");
        }

        var bodyBytes = Array.Empty<byte>();
        if (Request?.Body != null)
        {
            Request.EnableBuffering();
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            bodyBytes = ms.ToArray();
            if (Request.Body.CanSeek)
            {
                Request.Body.Position = 0;
            }
        }

        if (bodyBytes.Length == 0 && interaction != null)
        {
            bodyBytes = JsonSerializer.SerializeToUtf8Bytes(interaction, JsonOptions);
        }

        if (!_securityService.VerifySignature(signature, timestamp, bodyBytes, publicKey))
        {
            _logger.Warn("Discord interaction rejected: signature verification failed");
            return Unauthorized("Invalid request signature");
        }

        if (interaction == null && bodyBytes.Length > 0)
        {
            try
            {
                interaction = JsonSerializer.Deserialize<DiscordInteraction>(bodyBytes, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to deserialize Discord interaction body");
                return BadRequest("Invalid interaction JSON");
            }
        }

        if (interaction == null)
        {
            return BadRequest("Missing interaction payload");
        }

        if (interaction.Type == DiscordInteractionType.Ping)
        {
            return Ok(new DiscordInteractionResponse
            {
                Type = DiscordInteractionResponseType.Pong,
            });
        }

        var result = await _interactionHandler.HandleInteractionAsync(interaction, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}
