using System;
using System.Collections.Generic;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Seedarr.Http;

namespace Seedarr.Api.V1.Config;

[V1ApiController("config/ai")]
public class AiConfigController : ConfigController<AiConfigResource>
{
    private static readonly HashSet<string> AllowedActiveProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "RuleHeuristic",
        "Ollama",
        "Gemini",
        "Onnx",
    };

    public AiConfigController(IConfigService configService)
        : base(configService)
    {
        SharedValidator.RuleFor(c => c.ActiveAiProvider)
            .Must(p => !string.IsNullOrWhiteSpace(p) && AllowedActiveProviders.Contains(p.Trim()))
            .WithMessage("ActiveAiProvider must be one of: RuleHeuristic, Ollama, Gemini, Onnx.");

        SharedValidator.RuleFor(c => c.OllamaHost)
            .Must(IsValidOllamaHost)
            .WithMessage("OllamaHost must be a valid http or https URL.");

        SharedValidator.RuleFor(c => c.OnnxModelPath)
            .NotEmpty()
            .WithMessage("OnnxModelPath is required.");
    }

    protected override AiConfigResource ToResource(IConfigService model)
    {
        return AiConfigResourceMapper.ToResource(model);
    }

    [Authorize(Policy = Policies.AdminOnly)]
    public override ActionResult<AiConfigResource> SaveConfig(int? id, [FromBody] AiConfigResource resource)
    {
        var idValidationError = ValidateSaveConfigId(id, resource);
        if (idValidationError != null)
        {
            return idValidationError;
        }

        if (resource == null)
        {
            return BadRequest("Request body cannot be empty.");
        }

        var existingGeminiKey = _configService.GetValue("GeminiApiKey", string.Empty);
        if (string.IsNullOrWhiteSpace(resource.GeminiApiKey) ||
            resource.GeminiApiKey == "(unchanged)" ||
            resource.GeminiApiKey == GeneralConfigResourceMapper.GetMaskedApiKey(existingGeminiKey))
        {
            resource.GeminiApiKey = existingGeminiKey;
        }

        var actionResult = base.SaveConfig(id, resource);
        if (actionResult.Value != null)
        {
            actionResult.Value.GeminiApiKey =
                GeneralConfigResourceMapper.GetMaskedApiKey(actionResult.Value.GeminiApiKey);
        }

        return actionResult;
    }

    private static bool IsValidOllamaHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return Uri.TryCreate(host.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
