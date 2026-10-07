using NzbDrone.Core.Configuration;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Config;

public class AiConfigResource : RestResource
{
    public string ActiveAiProvider { get; set; }

    public string OllamaHost { get; set; }

    public string OllamaModel { get; set; }

    public string GeminiApiKey { get; set; }

    public string GeminiModel { get; set; }

    public string OnnxModelPath { get; set; }

    public bool EnableCopilotButton { get; set; }

    public bool EnableNaturalSearch { get; set; }

    public bool EnableSwarmDiagnostics { get; set; }
}

public static class AiConfigResourceMapper
{
    public const string DefaultActiveAiProvider = "RuleHeuristic";
    public const string DefaultOllamaHost = "http://127.0.0.1:11434";
    public const string DefaultOllamaModel = "llama3";
    public const string DefaultGeminiModel = "gemini-2.0-flash";
    public const string DefaultOnnxModelPath = "/config/models/seedarr-ai.onnx";

    public static AiConfigResource ToResource(IConfigService model)
    {
        var geminiApiKey = model.GetValue("GeminiApiKey", string.Empty);

        return new AiConfigResource
        {
            ActiveAiProvider = model.GetValue("ActiveAiProvider", DefaultActiveAiProvider),
            OllamaHost = model.GetValue("OllamaHost", DefaultOllamaHost),
            OllamaModel = model.GetValue("OllamaModel", DefaultOllamaModel),
            GeminiApiKey = GeneralConfigResourceMapper.GetMaskedApiKey(geminiApiKey),
            GeminiModel = model.GetValue("GeminiModel", DefaultGeminiModel),
            OnnxModelPath = model.GetValue("OnnxModelPath", DefaultOnnxModelPath),
            EnableCopilotButton = model.GetValueBoolean("EnableCopilotButton", true),
            EnableNaturalSearch = model.GetValueBoolean("EnableNaturalSearch", true),
            EnableSwarmDiagnostics = model.GetValueBoolean("EnableSwarmDiagnostics", true),
        };
    }
}
