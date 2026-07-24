using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration.Webhook;
using Seedarr.Http;

namespace Seedarr.Api.V1.ArrIntegration;

[V1ApiController("webhook")]
public class WebhookReceiverController : Controller
{
    private const string SecretHeader = "X-Seedarr-Secret";

    private readonly IArrWebhookService _webhookService;

    public WebhookReceiverController(IArrWebhookService webhookService)
    {
        _webhookService = webhookService;
    }

    [AllowAnonymous]
    [HttpPost("arr")]
    public ActionResult<ArrWebhookResult> ReceiveArrWebhook([FromBody] ArrWebhookPayload payload)
    {
        var secret = Request.Headers[SecretHeader].ToString();
        if (!_webhookService.ValidateWebhookSecret(secret))
        {
            return Unauthorized();
        }

        var result = _webhookService.ProcessWebhook(payload);
        return Ok(result);
    }
}
