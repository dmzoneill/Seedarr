// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;
using NzbDrone.Core.Configuration;
using Seedarr.Http.Security;

namespace Seedarr.Http.Authentication;

public class ForwardAuthOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ForwardAuth";

    public string UsernameHeaders { get; set; } = "X-authentik-username;Remote-User;X-Forwarded-User";

    public string EmailHeaders { get; set; } = "X-authentik-email;Remote-Email;X-Forwarded-Email";

    public string DisplayNameHeaders { get; set; } = "X-authentik-name;Remote-Name;X-Forwarded-Preferred-Username";

    public string GroupsHeaders { get; set; } = "X-authentik-groups;Remote-Groups;X-Forwarded-Groups";

    /// <summary>
    /// Comma- or semicolon-separated list of trusted proxy IP addresses or CIDR ranges.
    /// Default loopback addresses (127.0.0.1, ::1) are always trusted.
    /// </summary>
    public string TrustedProxies { get; set; } = string.Empty;

    /// <summary>
    /// Comma-, semicolon-, or pipe-separated group names that grant Admin role.
    /// </summary>
    public string AdminGroups { get; set; } = "admin;admins;administrator;administrators";

    /// <summary>
    /// Default role for authenticated users who do not match AdminGroups. Defaults to "User".
    /// </summary>
    public string DefaultRole { get; set; } = "User";
}

public class ForwardAuthHandler : AuthenticationHandler<ForwardAuthOptions>
{
    private static readonly Logger NLogLogger = LogManager.GetCurrentClassLogger();
    private readonly IConfigFileProvider _configFileProvider;

    public ForwardAuthHandler(
        IOptionsMonitor<ForwardAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfigFileProvider configFileProvider = null)
        : base(options, logger, encoder)
    {
        _configFileProvider = configFileProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var remoteIp = Context.Connection.RemoteIpAddress;
        if (remoteIp == null)
        {
            NLogLogger.Warn("ForwardAuth authentication rejected: RemoteIpAddress is null.");
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var trustedProxies = CombineProxies(Options.TrustedProxies, _configFileProvider?.TrustedProxies);
        if (!IpSecurityHelper.IsTrustedProxy(remoteIp, trustedProxies))
        {
            NLogLogger.Warn("ForwardAuth authentication rejected: RemoteIpAddress {0} is not a trusted proxy.", remoteIp);
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var username = GetHeaderValue(Options.UsernameHeaders);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var email = GetHeaderValue(Options.EmailHeaders);
        var displayName = GetHeaderValue(Options.DisplayNameHeaders) ?? username;
        var rawGroupsStr = GetHeaderValue(Options.GroupsHeaders);
        var groups = !string.IsNullOrWhiteSpace(rawGroupsStr)
            ? rawGroupsStr.Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        var adminGroups = (Options.AdminGroups ?? "admin;admins;administrator;administrators")
            .Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var isAdmin = groups.Any(g => adminGroups.Contains(g, StringComparer.OrdinalIgnoreCase));
        var role = isAdmin ? "Admin" : (!string.IsNullOrWhiteSpace(Options.DefaultRole) ? Options.DefaultRole : "User");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Name, username),
            new("DisplayName", displayName),
            new(ClaimTypes.Role, role),
        };

        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        foreach (var group in groups)
        {
            claims.Add(new Claim("Group", group));
        }

        var identity = new ClaimsIdentity(claims, ForwardAuthOptions.DefaultScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ForwardAuthOptions.DefaultScheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static string CombineProxies(string optProxies, string configProxies)
    {
        if (string.IsNullOrWhiteSpace(optProxies))
        {
            return configProxies ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(configProxies))
        {
            return optProxies ?? string.Empty;
        }

        return $"{optProxies},{configProxies}";
    }

    private string GetHeaderValue(string headerNames)
    {
        var candidates = headerNames.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in candidates)
        {
            if (Request.Headers.TryGetValue(name.Trim(), out var val) && !string.IsNullOrWhiteSpace(val.FirstOrDefault()))
            {
                return val.FirstOrDefault().Trim();
            }
        }

        return null;
    }
}
