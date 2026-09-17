using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    private readonly ConcurrentDictionary<string, IdentityProviderDefinition> _pendingRetryProviders = new();

    public DynamicAuthSchemeManager(
        IServiceProvider serviceProvider,
        IIdentityProviderRepository identityProviderRepository)
    {
        _serviceProvider = serviceProvider;
        _identityProviderRepository = identityProviderRepository;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public bool HasPendingRetry(string providerId) => _pendingRetryProviders.ContainsKey(providerId);

    public IReadOnlyCollection<string> PendingRetryProviderIds => _pendingRetryProviders.Keys.ToList();

    public async Task InitializeConfiguredProvidersAsync()
    {
        try
        {
            var enabledProviders = _identityProviderRepository.GetEnabled();
            if (enabledProviders == null)
            {
                return;
            }

            foreach (var provider in enabledProviders)
            {
                if (provider.ProviderType == IdentityProviderType.Oidc || provider.ProviderType == IdentityProviderType.Social)
                {
                    try
                    {
                        await RegisterOrUpdateOidcProviderAsync(provider);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to initialize dynamic authentication provider '{0}' ({1}) during startup. Scheduling background retry.", provider.Name, provider.ProviderId);
                        ScheduleRetry(provider);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize configured dynamic authentication schemes");
        }
    }

    public void ScheduleRetry(IdentityProviderDefinition provider, int delaySeconds = 30)
    {
        if (provider == null || string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            return;
        }

        _pendingRetryProviders[provider.ProviderId] = provider;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                if (_pendingRetryProviders.TryGetValue(provider.ProviderId, out var pending))
                {
                    var current = _identityProviderRepository.FindByProviderId(provider.ProviderId);
                    if (current != null && current.IsEnabled)
                    {
                        await RegisterOrUpdateOidcProviderAsync(current);
                        _pendingRetryProviders.TryRemove(provider.ProviderId, out _);
                        _logger.Info("Successfully registered dynamic OIDC authentication scheme on retry: Oidc_{0} ({1})", current.ProviderId, current.Name);
                    }
                    else
                    {
                        _pendingRetryProviders.TryRemove(provider.ProviderId, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Retry registration failed for OIDC provider {0} ({1}), will retry again", provider.ProviderId, provider.Name);
                ScheduleRetry(provider, Math.Min(delaySeconds * 2, 300));
            }
        });
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

        var existingScheme = await schemeProvider.GetSchemeAsync(schemeName);
        if (existingScheme != null)
        {
            schemeProvider.RemoveScheme(schemeName);
        }

        InvalidateHandlerCache(schemeName);

        if (string.IsNullOrWhiteSpace(provider.IssuerUrl) || string.IsNullOrWhiteSpace(provider.ClientId))
        {
            throw new ArgumentException("IssuerUrl and ClientId are required to register an OIDC provider.");
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

        if (!string.IsNullOrWhiteSpace(provider.MetadataUrl))
        {
            options.MetadataAddress = provider.MetadataUrl;
        }

        try
        {
            if (oidcPostConfigure != null)
            {
                oidcPostConfigure.PostConfigure(schemeName, options);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Post-configuration for OIDC provider '{0}' ({1}) encountered an error. Proceeding with deferred metadata discovery.", provider.Name, provider.ProviderId);
        }

        oidcOptionsCache.TryAdd(schemeName, options);

        var newScheme = new AuthenticationScheme(schemeName, provider.Name, typeof(OpenIdConnectHandler));
        schemeProvider.AddScheme(newScheme);

        _pendingRetryProviders.TryRemove(provider.ProviderId, out _);

        _logger.Info("Registered dynamic OIDC authentication scheme: {0} ({1})", schemeName, provider.Name);
    }

    public async Task RemoveProviderSchemeAsync(string providerId)
    {
        var schemeName = $"Oidc_{providerId}";
        var schemeProvider = _serviceProvider.GetService<IAuthenticationSchemeProvider>();
        var oidcOptionsCache = _serviceProvider.GetService<IOptionsMonitorCache<OpenIdConnectOptions>>();

        _pendingRetryProviders.TryRemove(providerId, out _);

        if (schemeProvider != null)
        {
            schemeProvider.RemoveScheme(schemeName);
        }

        if (oidcOptionsCache != null)
        {
            oidcOptionsCache.TryRemove(schemeName);
        }

        InvalidateHandlerCache(schemeName);

        _logger.Info("Removed dynamic authentication scheme: {0}", schemeName);
        await Task.CompletedTask;
    }

    private void InvalidateHandlerCache(string schemeName)
    {
        try
        {
            var handlerProvider = _serviceProvider.GetService<IAuthenticationHandlerProvider>();
            if (handlerProvider != null)
            {
                var handlerMapField = handlerProvider.GetType().GetField("_handlerMap", BindingFlags.NonPublic | BindingFlags.Instance);
                if (handlerMapField?.GetValue(handlerProvider) is System.Collections.IDictionary handlerMap)
                {
                    lock (handlerMap.SyncRoot)
                    {
                        handlerMap.Remove(schemeName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Trace(ex, "Failed to invalidate handler cache for scheme {0}", schemeName);
        }
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
