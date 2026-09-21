using Microsoft.AspNetCore.Mvc;
using Seedarr.Http;

namespace Seedarr.Api.V1.Config;

[V1ApiController("config/ai")]
public class AiConfigController : Controller
{
    [HttpGet]
    public ActionResult<object> GetConfig()
    {
        return Ok(new
        {
            id = 1,
            enabled = false,
            activeProvider = "none",
            ollamaEndpoint = "http://localhost:11434",
            ollamaModel = "llama3:latest",
            geminiApiKey = string.Empty,
            geminiModel = "gemini-1.5-flash",
            onnxModelPath = "/config/models/seedarr-ai.onnx",
            enableCopilot = false,
            enableAnomalyDetection = false,
            enableNaturalLanguageSearch = false,
            enableAutoRemediation = false
        });
    }

    [HttpPut("{id}")]
    public ActionResult<object> UpdateConfig(int id, [FromBody] object config)
    {
        return Ok(config);
    }
}
