using System;
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
        var expected = _configFileProvider?.ApiKey;

        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        var provided = Request.Headers[ApiKeyHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(provided) && Request.Headers.TryGetValue("ApiKey", out var altHeader))
        {
            provided = altHeader.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(provided) && Request.Query.TryGetValue("apikey", out var qKey))
        {
            provided = qKey.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(provided) && Request.Query.TryGetValue("api_key", out var qKey2))
        {
            provided = qKey2.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(provided) && Request.Query.TryGetValue("access_token", out var qToken))
        {
            provided = qToken.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(provided) &&
            Request.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            provided = authHeader.ToString()["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }

        Span<byte> hashA = stackalloc byte[32];
        Span<byte> hashB = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(provided), hashA);
        SHA256.HashData(Encoding.UTF8.GetBytes(expected), hashB);
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }
}
