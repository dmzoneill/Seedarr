using Microsoft.AspNetCore.Mvc;
using Seedarr.Http;

namespace Seedarr.Api.V1.Ai;

[V1ApiController("ai")]
public class AiController : Controller
{
    [HttpGet("status")]
    public ActionResult<object> GetStatus()
    {
        return Ok(new
        {
            activeProviderId = "none",
            displayName = "Disabled",
            version = "1.0",
            description = "AI features not configured",
            capabilities = new
            {
                supportsNaturalLanguageSearch = false,
                supportsReleaseNameParsing = false,
                supportsDiagnosticCopilot = false,
                supportsMalwareAnomalyDetection = false,
                supportsSwarmOptimization = false,
                supportsSyntheticRatioForecasting = false,
                supportsStreamingChat = false,
                supportsLocalInference = false,
            },
            healthy = false,
            latencyMs = 0,
            error = (string)null
        });
    }

    [HttpGet("providers")]
    public ActionResult<object> GetProviders()
    {
        return Ok(new object[0]);
    }
}
