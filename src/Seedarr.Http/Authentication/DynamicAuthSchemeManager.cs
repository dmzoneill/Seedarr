using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NLog;
using NzbDrone.Core.Authentication;

namespace Seedarr.Http.Authentication;

public class DynamicAuthSchemeManager : IDynamicAuthSchemeManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IIdentityProviderRepository _identityProviderRepository;
    private readonly Logger _logger;

    public DynamicAuthSchemeManager(
        IServiceProvider serviceProvider,
        IIdentityProviderRepository identityProviderRepository)
    {
        _serviceProvider = serviceProvider;
        _identityProviderRepository = identityProviderRepository;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public async Task InitializeConfiguredProvidersAsync()
    {
        try
        {
            var enabledProviders = _identityProviderRepository.GetEnabled();
            foreach (var provider in enabledProviders)
            {
                if (provider.ProviderType == IdentityProviderType.Oidc || provider.ProviderType == IdentityProviderType.Social)
                {
                    await RegisterOrUpdateOidcProviderAsync(provider);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize configured dynamic authentication schemes");
        }
    }

    public async Task RegisterOrUpdateOidcProviderAsync(IdentityProviderDefinition provider)
    {
        var schemeName = $"Oidc_{provider.ProviderId}";
        var oidcOptionsCache = _serviceProvider.GetService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        var oidcPostConfigure = _serviceProvider.GetService<IPostConfigureOptions<OpenIdConnectOptions>>();
        var schemeProvider = _serviceProvider.GetService<IAuthenticationSchemeProvider>();
        var dataProtection = _serviceProvider.GetService<IDataProtectionProvider>();

        if (oidcOptionsCache == null || schemeProvider == null)
        {
            return;
        }

        oidcOptionsCache.TryRemove(schemeName);

        if (string.IsNullOrWhiteSpace(provider.IssuerUrl) || string.IsNullOrWhiteSpace(provider.ClientId))
        {
            return;
        }

        var options = new OpenIdConnectOptions
        {
            SignInScheme = "Cookies",
            Authority = provider.IssuerUrl.TrimEnd('/'),
            ClientId = provider.ClientId,
            ClientSecret = provider.ClientSecretEncrypted ?? string.Empty,
            ResponseType = OpenIdConnectResponseType.Code,
            ResponseMode = OpenIdConnectResponseMode.Query,
            GetClaimsFromUserInfoEndpoint = true,
            SaveTokens = true,
            CallbackPath = $"/signin-oidc-{provider.ProviderId}",
            RequireHttpsMetadata = provider.IssuerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
            DataProtectionProvider = dataProtection,
            Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    if (context.Request.IsHttps ||
                        string.Equals(context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(context.ProtocolMessage.RedirectUri) &&
                            context.ProtocolMessage.RedirectUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                        {
                            context.ProtocolMessage.RedirectUri = "https://" + context.ProtocolMessage.RedirectUri["http://".Length..];
                        }
                    }

                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var claims = context.Principal?.Claims.ToList() ?? new List<Claim>();
                    var sub = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "sub")?.Value
                              ?? Guid.NewGuid().ToString();
                    var username = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name || c.Type == "preferred_username" || c.Type == "nickname")?.Value
                                   ?? sub;
                    var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email")?.Value;
                    var displayName = claims.FirstOrDefault(c => c.Type == "name")?.Value ?? username;

                    var userClaims = new List<Claim>
                    {
                        new(ClaimTypes.NameIdentifier, sub),
                        new(ClaimTypes.Name, username),
                        new("DisplayName", displayName),
                    };

                    var assignedRoles = ResolveRoles(claims, provider.RoleMappingRules);
                    foreach (var role in assignedRoles)
                    {
                        userClaims.Add(new Claim(ClaimTypes.Role, role));
                    }

                    if (!string.IsNullOrEmpty(email))
                    {
                        userClaims.Add(new Claim(ClaimTypes.Email, email));
                    }

                    var identity = new ClaimsIdentity(userClaims, "Cookies");
                    context.Principal = new ClaimsPrincipal(identity);

                    return Task.CompletedTask;
                },
            },
        };

        options.Scope.Clear();
        var scopes = (provider.Scopes ?? "openid profile email").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var scope in scopes)
        {
            options.Scope.Add(scope);
        }

        if (oidcPostConfigure != null)
        {
            oidcPostConfigure.PostConfigure(schemeName, options);
        }

        oidcOptionsCache.TryAdd(schemeName, options);

        var existingScheme = await schemeProvider.GetSchemeAsync(schemeName);
        if (existingScheme != null)
        {
            schemeProvider.RemoveScheme(schemeName);
        }

        var newScheme = new AuthenticationScheme(schemeName, provider.Name, typeof(OpenIdConnectHandler));
        schemeProvider.AddScheme(newScheme);

        _logger.Info("Registered dynamic OIDC authentication scheme: {0} ({1})", schemeName, provider.Name);
    }

    public async Task RemoveProviderSchemeAsync(string providerId)
    {
        var schemeName = $"Oidc_{providerId}";
        var schemeProvider = _serviceProvider.GetService<IAuthenticationSchemeProvider>();
        var oidcOptionsCache = _serviceProvider.GetService<IOptionsMonitorCache<OpenIdConnectOptions>>();

        if (schemeProvider != null)
        {
            schemeProvider.RemoveScheme(schemeName);
        }

        if (oidcOptionsCache != null)
        {
            oidcOptionsCache.TryRemove(schemeName);
        }

        _logger.Info("Removed dynamic authentication scheme: {0}", schemeName);
        await Task.CompletedTask;
    }

    private static readonly HashSet<string> RoleClaimTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ClaimTypes.Role,
        "role",
        "roles",
        "groups",
        "group",
        "realm_access.roles",
        "realm_access",
        "resource_access",
        "cognito:groups",
        "memberOf",
        "user_roles",
        "permissions",
        "authorities",
    };

    public static List<string> ExtractCandidateClaimValues(IEnumerable<Claim> claims)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (claims == null)
        {
            return values.ToList();
        }

        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.Value))
            {
                continue;
            }

            var type = claim.Type;
            if (!RoleClaimTypeNames.Contains(type) &&
                !type.EndsWith("/role", StringComparison.OrdinalIgnoreCase) &&
                !type.EndsWith("/roles", StringComparison.OrdinalIgnoreCase) &&
                !type.EndsWith("/groups", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = claim.Value.Trim();

            if (raw.StartsWith("[") && raw.EndsWith("]"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            if (elem.ValueKind == JsonValueKind.String)
                            {
                                var s = elem.GetString()?.Trim();
                                if (!string.IsNullOrEmpty(s))
                                {
                                    values.Add(s);
                                }
                            }
                        }

                        continue;
                    }
                }
                catch
                {
                    // Fall back to raw string
                }
            }

            if (raw.StartsWith("{") && raw.EndsWith("}"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("roles", out var rolesElem) && rolesElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in rolesElem.EnumerateArray())
                        {
                            if (elem.ValueKind == JsonValueKind.String)
                            {
                                var s = elem.GetString()?.Trim();
                                if (!string.IsNullOrEmpty(s))
                                {
                                    values.Add(s);
                                }
                            }
                        }
                    }

                    if (root.TryGetProperty("groups", out var groupsElem) && groupsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in groupsElem.EnumerateArray())
                        {
                            if (elem.ValueKind == JsonValueKind.String)
                            {
                                var s = elem.GetString()?.Trim();
                                if (!string.IsNullOrEmpty(s))
                                {
                                    values.Add(s);
                                }
                            }
                        }
                    }

                    continue;
                }
                catch
                {
                    // Fall back to raw string
                }
            }

            if (raw.Contains(','))
            {
                var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var part in parts)
                {
                    if (!string.IsNullOrEmpty(part))
                    {
                        values.Add(part);
                    }
                }
            }

            values.Add(raw);
        }

        return values.ToList();
    }

    public static List<string> ResolveRoles(IEnumerable<Claim> claims, string roleMappingRulesJson)
    {
        var assignedRoles = new List<string>();
        var candidateValues = ExtractCandidateClaimValues(claims);

        if (!string.IsNullOrWhiteSpace(roleMappingRulesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(roleMappingRulesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var roleName = prop.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(roleName))
                        {
                            continue;
                        }

                        var patterns = new List<string>();
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var pat = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(pat))
                            {
                                patterns.Add(pat);
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                                {
                                    patterns.Add(item.GetString());
                                }
                            }
                        }

                        var matched = false;
                        foreach (var pattern in patterns)
                        {
                            try
                            {
                                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                                if (candidateValues.Any(val => regex.IsMatch(val)))
                                {
                                    matched = true;
                                    break;
                                }
                            }
                            catch
                            {
                                if (candidateValues.Any(val => string.Equals(val, pattern, StringComparison.OrdinalIgnoreCase)))
                                {
                                    matched = true;
                                    break;
                                }
                            }
                        }

                        if (matched && !assignedRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
                        {
                            assignedRoles.Add(roleName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Warn(ex, "Failed to parse RoleMappingRules JSON: {0}", roleMappingRulesJson);
            }
        }

        if (assignedRoles.Count == 0)
        {
            assignedRoles.Add("User");
        }

        return assignedRoles;
    }
}
