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

namespace Seedarr.Http.Authentication;

public class ForwardAuthOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ForwardAuth";

    public string UsernameHeaders { get; set; } = "X-authentik-username;Remote-User;X-Forwarded-User";

    public string EmailHeaders { get; set; } = "X-authentik-email;Remote-Email;X-Forwarded-Email";

    public string DisplayNameHeaders { get; set; } = "X-authentik-name;Remote-Name;X-Forwarded-Preferred-Username";

    public string GroupsHeaders { get; set; } = "X-authentik-groups;Remote-Groups;X-Forwarded-Groups";
}

public class ForwardAuthHandler : AuthenticationHandler<ForwardAuthOptions>
{
    public ForwardAuthHandler(
        IOptionsMonitor<ForwardAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
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

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Name, username),
            new("DisplayName", displayName),
            new(ClaimTypes.Role, "Admin"),
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
