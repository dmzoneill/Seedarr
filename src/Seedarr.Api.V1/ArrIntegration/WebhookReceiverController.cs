using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration.Webhook;
using NzbDrone.Core.Configuration;
using Seedarr.Http;

namespace Seedarr.Api.V1.ArrIntegration;

[V1ApiController("webhook")]
public class WebhookReceiverController : Controller
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly IArrWebhookService _webhookService;
    private readonly IConfigFileProvider _configFileProvider;

    public WebhookReceiverController(IArrWebhookService webhookService, IConfigFileProvider configFileProvider)
    {
        _webhookService = webhookService;
        _configFileProvider = configFileProvider;
    }

    [HttpPost("arr")]
    public ActionResult<ArrWebhookResult> ReceiveArrWebhook([FromBody] ArrWebhookPayload payload)
    {
        if (!IsApiKeyValid())
        {
            return Unauthorized();
        }

        var result = _webhookService.ProcessWebhook(payload);
        return Ok(result);
    }

    // Explicit check so the endpoint stays protected even when instance-level
    // authentication is disabled (e.g. test environments).
    private bool IsApiKeyValid()
    {
        var provided = Request.Headers[ApiKeyHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }

        var expected = _configFileProvider.ApiKey ?? string.Empty;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
    }
}
