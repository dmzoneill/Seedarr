// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Authentication;

namespace Seedarr.Api.V1.Auth;

public class LoginRequestResource
{
    public string Username { get; set; }

    public string Password { get; set; }

    public bool RememberMe { get; set; } = true;

    public string ReturnUrl { get; set; }
}

public class CurrentUserResource
{
    public int? Id { get; set; }

    public string Identifier { get; set; }

    public string Username { get; set; }

    public string Email { get; set; }

    public string DisplayName { get; set; }

    public List<string> Roles { get; set; } = new();

    public string AvatarUrl { get; set; }

    public bool IsAuthenticated { get; set; }

    public bool RequiresPassword { get; set; } = true;

    public bool AuthenticationEnabled { get; set; }

    public string ReturnUrl { get; set; }
}

public class AuthProviderResource
{
    public int Id { get; set; }

    public string ProviderId { get; set; }

    public string Name { get; set; }

    public IdentityProviderType ProviderType { get; set; }

    public string IconUrl { get; set; }

    public string ButtonText { get; set; }

    public string LoginUrl { get; set; }
}
