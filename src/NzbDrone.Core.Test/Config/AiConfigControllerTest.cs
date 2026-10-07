using System.Collections.Generic;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Seedarr.Api.V1.Config;

namespace NzbDrone.Core.Test.Config;

[TestFixture]
public class AiConfigControllerTest
{
    private IConfigService _configService;
    private AiConfigController _controller;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _configService.GetValue("GeminiApiKey", string.Empty).Returns(string.Empty);
        _controller = new AiConfigController(_configService);
    }

    [Test]
    public void GetConfig_should_map_frontend_contract_fields()
    {
        _configService.GetValue("ActiveAiProvider", AiConfigResourceMapper.DefaultActiveAiProvider)
            .Returns("Ollama");
        _configService.GetValue("OllamaHost", AiConfigResourceMapper.DefaultOllamaHost)
            .Returns("http://127.0.0.1:11434");
        _configService.GetValue("OllamaModel", AiConfigResourceMapper.DefaultOllamaModel)
            .Returns("llama3.2");
        _configService.GetValue("GeminiModel", AiConfigResourceMapper.DefaultGeminiModel)
            .Returns("gemini-2.0-flash");
        _configService.GetValue("OnnxModelPath", AiConfigResourceMapper.DefaultOnnxModelPath)
            .Returns("/config/models/seedarr-ai.onnx");
        _configService.GetValueBoolean("EnableCopilotButton", true).Returns(false);
        _configService.GetValueBoolean("EnableNaturalSearch", true).Returns(true);
        _configService.GetValueBoolean("EnableSwarmDiagnostics", true).Returns(false);

        var resource = _controller.GetConfig();

        Assert.That(resource.Id, Is.EqualTo(1));
        Assert.That(resource.ActiveAiProvider, Is.EqualTo("Ollama"));
        Assert.That(resource.OllamaHost, Is.EqualTo("http://127.0.0.1:11434"));
        Assert.That(resource.OllamaModel, Is.EqualTo("llama3.2"));
        Assert.That(resource.EnableCopilotButton, Is.False);
        Assert.That(resource.EnableSwarmDiagnostics, Is.False);
    }

    [Test]
    public void SaveConfig_should_persist_values_and_return_accepted()
    {
        var resource = new AiConfigResource
        {
            Id = 1,
            ActiveAiProvider = "Gemini",
            OllamaHost = "http://127.0.0.1:11434",
            OllamaModel = "llama3",
            GeminiApiKey = "AIza-test-key",
            GeminiModel = "gemini-2.0-flash",
            OnnxModelPath = "/config/models/seedarr-ai.onnx",
            EnableCopilotButton = true,
            EnableNaturalSearch = true,
            EnableSwarmDiagnostics = false,
        };

        var result = _controller.SaveConfig(1, resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            d.ContainsKey("ActiveAiProvider") &&
            (string)d["ActiveAiProvider"] == "Gemini" &&
            d.ContainsKey("OllamaHost") &&
            d.ContainsKey("EnableSwarmDiagnostics") &&
            (bool)d["EnableSwarmDiagnostics"] == false));
    }

    [Test]
    public void SaveConfig_should_return_bad_request_for_invalid_provider()
    {
        var resource = new AiConfigResource
        {
            ActiveAiProvider = "none",
            OllamaHost = "http://127.0.0.1:11434",
            OllamaModel = "llama3",
            GeminiModel = "gemini-2.0-flash",
            OnnxModelPath = "/config/models/seedarr-ai.onnx",
        };

        var result = _controller.SaveConfig(1, resource);

        Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
        var badRequest = (BadRequestObjectResult)result.Result;
        var errors = (IList<ValidationFailure>)badRequest.Value;
        Assert.That(errors, Has.Some.Matches<ValidationFailure>(e =>
            e.PropertyName == nameof(AiConfigResource.ActiveAiProvider)));
    }

    [Test]
    public void SaveConfig_should_preserve_masked_gemini_api_key()
    {
        _configService.GetValue("GeminiApiKey", string.Empty).Returns("AIza-secret");

        var resource = new AiConfigResource
        {
            Id = 1,
            ActiveAiProvider = "Gemini",
            OllamaHost = "http://127.0.0.1:11434",
            OllamaModel = "llama3",
            GeminiApiKey = GeneralConfigResourceMapper.GetMaskedApiKey("AIza-secret"),
            GeminiModel = "gemini-2.0-flash",
            OnnxModelPath = "/config/models/seedarr-ai.onnx",
        };

        var result = _controller.SaveConfig(1, resource);

        Assert.That(result.Result, Is.InstanceOf<AcceptedResult>());
        _configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["GeminiApiKey"] == "AIza-secret"));
    }
}
