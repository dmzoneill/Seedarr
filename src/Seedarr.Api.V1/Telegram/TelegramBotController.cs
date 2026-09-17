using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Notifications.Telegram;
using Seedarr.Http;

namespace Seedarr.Api.V1.Telegram;

[V1ApiController("telegram")]
public class TelegramBotController : Controller
{
    public const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

    private readonly IConfigService _configService;
    private readonly ITelegramUpdateHandler _telegramUpdateHandler;
    private readonly Logger _logger;

    public TelegramBotController(
        IConfigService configService,
        ITelegramUpdateHandler telegramUpdateHandler)
    {
        _configService = configService;
        _telegramUpdateHandler = telegramUpdateHandler;
        _logger = LogManager.GetCurrentClassLogger();
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveWebhook([FromBody] TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        if (!IsSecretTokenValid())
        {
            _logger.Warn("Telegram webhook rejected: missing or invalid secret token");
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (update != null)
        {
            var result = await _telegramUpdateHandler.HandleUpdateAsync(update, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(body))
        {
            var result = await _telegramUpdateHandler.HandleUpdateAsync(body, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }

        return Ok();
    }

    private bool IsSecretTokenValid()
    {
        var providedToken = Request?.Headers?[SecretTokenHeader].FirstOrDefault();
        var configuredToken = _configService?.TelegramSecretToken;

        if (string.IsNullOrWhiteSpace(providedToken) || string.IsNullOrWhiteSpace(configuredToken))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedToken);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredToken);

        return CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}
