using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.ArrIntegration.Webhook;
using Seedarr.Http;

namespace Seedarr.Api.V1.ArrIntegration;

[V1ApiController("webhook")]
public class WebhookReceiverController : Controller
{
    private readonly IArrWebhookService _webhookService;

    public WebhookReceiverController(IArrWebhookService webhookService)
    {
        _webhookService = webhookService;
    }

    [HttpPost("arr")]
    public ActionResult<ArrWebhookResult> ReceiveArrWebhook([FromBody] ArrWebhookPayload payload)
    {
        var result = _webhookService.ProcessWebhook(payload);
        return Ok(result);
    }
}
