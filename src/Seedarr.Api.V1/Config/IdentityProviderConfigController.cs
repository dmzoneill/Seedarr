using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.Authentication;
using Seedarr.Http;
using Seedarr.Http.Authentication;
using Seedarr.Http.REST;

namespace Seedarr.Api.V1.Config;

public class IdentityProviderResource : RestResource
{
    public string ProviderId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public IdentityProviderType ProviderType { get; set; } = IdentityProviderType.Oidc;

    public bool IsEnabled { get; set; } = true;

    public string ClientId { get; set; }

    public string ClientSecret { get; set; }

    public string IssuerUrl { get; set; }

    public string MetadataUrl { get; set; }

    public string Scopes { get; set; } = "openid profile email";

    public string Certificate { get; set; }

    public string RoleMappingRules { get; set; }

    public string IconUrl { get; set; }

    public string ButtonText { get; set; }
}

[V1ApiController("config/auth/providers")]
public class IdentityProviderConfigController : RestController<IdentityProviderResource>
{
    private const string MaskedSecret = "********";
    private const string AlternateMaskedSecret = "******";

    private static readonly HashSet<string> ReservedProviderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cookies",
        "SeedarrApiKey",
        "Bearer",
        "Basic",
    };

    private static readonly Regex ProviderIdRegex = new(@"^[a-zA-Z0-9_-]{2,32}$", RegexOptions.Compiled);

    private readonly IIdentityProviderService _providerService;
    private readonly IDynamicAuthSchemeManager _dynamicAuthManager;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public IdentityProviderConfigController(
        IIdentityProviderService providerService,
        IDynamicAuthSchemeManager dynamicAuthManager)
    {
        _providerService = providerService;
        _dynamicAuthManager = dynamicAuthManager;

        SharedValidator = new ResourceValidator<IdentityProviderResource>();
        SharedValidator.RuleFor(c => c.ProviderId)
            .NotEmpty()
            .WithMessage("'ProviderId' must not be empty.")
            .Matches(ProviderIdRegex)
            .WithMessage("'ProviderId' must be between 2 and 32 characters and contain only alphanumeric characters, underscores, or hyphens.")
            .Must(id => id == null || !ReservedProviderIds.Contains(id))
            .WithMessage("'ProviderId' cannot use a reserved scheme name.");

        SharedValidator.RuleFor(c => c.Name)
            .NotEmpty()
            .WithMessage("'Name' must not be empty.");

        SharedValidator.RuleFor(c => c.IssuerUrl)
            .Must(IsValidAbsoluteUri)
            .When(c => !string.IsNullOrEmpty(c.IssuerUrl))
            .WithMessage("'IssuerUrl' must be a valid absolute URI.");

        SharedValidator.RuleFor(c => c.MetadataUrl)
            .Must(IsValidAbsoluteUri)
            .When(c => !string.IsNullOrEmpty(c.MetadataUrl))
            .WithMessage("'MetadataUrl' must be a valid absolute URI.");
    }

    [HttpGet]
    [Authorize(Policy = Policies.Reader)]
    public ActionResult<List<IdentityProviderResource>> GetAll()
    {
        var providers = _providerService.GetAll();
        var resources = new List<IdentityProviderResource>();

        foreach (var p in providers)
        {
            resources.Add(ToResource(p));
        }

        return Ok(resources);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.Reader)]
    public ActionResult<IdentityProviderResource> GetById(int id)
    {
        var provider = _providerService.GetById(id);
        if (provider == null)
        {
            return NotFound();
        }

        return Ok(ToResource(provider));
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<IdentityProviderResource>> Create([FromBody] IdentityProviderResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var validationResult = await SharedValidator.ValidateAsync(resource);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var model = ToModel(resource);
        var created = _providerService.Add(model);

        if (created.IsEnabled)
        {
            try
            {
                await _dynamicAuthManager.RegisterOrUpdateOidcProviderAsync(created);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to register dynamic authentication scheme for provider: {0}", created.ProviderId);
                return BadRequest(new { message = $"Failed to register authentication scheme: {ex.Message}" });
            }
        }

        return Created($"/api/v1/config/auth/providers/{created.Id}", ToResource(created));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<IdentityProviderResource>> Update(int id, [FromBody] IdentityProviderResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var validationResult = await SharedValidator.ValidateAsync(resource);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var existing = _providerService.GetById(id);
        if (existing == null)
        {
            return NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;

        // If client secret is masked with asterisks or empty, preserve original
        if (string.IsNullOrWhiteSpace(model.ClientSecretEncrypted) ||
            model.ClientSecretEncrypted == MaskedSecret ||
            model.ClientSecretEncrypted == AlternateMaskedSecret)
        {
            model.ClientSecretEncrypted = existing.ClientSecretEncrypted;
        }

        var updated = _providerService.Update(model);

        if (updated.IsEnabled)
        {
            try
            {
                await _dynamicAuthManager.RegisterOrUpdateOidcProviderAsync(updated);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to update dynamic authentication scheme for provider: {0}", updated.ProviderId);
                return BadRequest(new { message = $"Failed to update authentication scheme: {ex.Message}" });
            }
        }
        else
        {
            try
            {
                await _dynamicAuthManager.RemoveProviderSchemeAsync(updated.ProviderId);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to remove dynamic authentication scheme for provider: {0}", updated.ProviderId);
                return BadRequest(new { message = $"Failed to remove authentication scheme: {ex.Message}" });
            }
        }

        return Ok(ToResource(updated));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult> Delete(int id)
    {
        var existing = _providerService.GetById(id);
        if (existing == null)
        {
            return NotFound();
        }

        _providerService.Delete(id);
        try
        {
            await _dynamicAuthManager.RemoveProviderSchemeAsync(existing.ProviderId);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to remove dynamic authentication scheme for provider: {0}", existing.ProviderId);
            return BadRequest(new { message = $"Failed to remove authentication scheme: {ex.Message}" });
        }

        return NoContent();
    }

    [HttpPost("test")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult> TestConnection([FromBody] IdentityProviderResource resource)
    {
        if (resource == null)
        {
            return BadRequest();
        }

        var model = ToModel(resource);
        if (resource.Id > 0 &&
            (string.IsNullOrWhiteSpace(model.ClientSecretEncrypted) ||
                model.ClientSecretEncrypted == MaskedSecret ||
                model.ClientSecretEncrypted == AlternateMaskedSecret))
        {
            var existing = _providerService.GetById(resource.Id);
            if (existing != null)
            {
                model.ClientSecretEncrypted = existing.ClientSecretEncrypted;
            }
        }

        var success = await _providerService.TestConnectionAsync(model);

        return Ok(new { success, message = success ? "Connection successful" : "Failed to reach provider endpoint" });
    }

    private static IdentityProviderResource ToResource(IdentityProviderDefinition model)
    {
        return new IdentityProviderResource
        {
            Id = model.Id,
            ProviderId = model.ProviderId,
            Name = model.Name,
            ProviderType = model.ProviderType,
            IsEnabled = model.IsEnabled,
            ClientId = model.ClientId,
            ClientSecret = string.IsNullOrEmpty(model.ClientSecretEncrypted) ? null : MaskedSecret,
            IssuerUrl = model.IssuerUrl,
            MetadataUrl = model.MetadataUrl,
            Scopes = model.Scopes,
            Certificate = model.Certificate,
            RoleMappingRules = model.RoleMappingRules,
            IconUrl = model.IconUrl,
            ButtonText = model.ButtonText,
        };
    }

    private static IdentityProviderDefinition ToModel(IdentityProviderResource resource)
    {
        return new IdentityProviderDefinition
        {
            Id = resource.Id,
            ProviderId = resource.ProviderId,
            Name = resource.Name,
            ProviderType = resource.ProviderType,
            IsEnabled = resource.IsEnabled,
            ClientId = resource.ClientId,
            ClientSecretEncrypted = resource.ClientSecret,
            IssuerUrl = resource.IssuerUrl,
            MetadataUrl = resource.MetadataUrl,
            Scopes = resource.Scopes,
            Certificate = resource.Certificate,
            RoleMappingRules = resource.RoleMappingRules,
            IconUrl = resource.IconUrl,
            ButtonText = resource.ButtonText,
        };
    }

    private static bool IsValidAbsoluteUri(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
