namespace Seedarr.Api.V1.Auth;

public static class Roles
{
    public const string Admin = NzbDrone.Core.Authentication.Roles.Admin;
    public const string User = NzbDrone.Core.Authentication.Roles.User;
    public const string ReadOnly = NzbDrone.Core.Authentication.Roles.ReadOnly;
}

public static class Policies
{
    public const string AdminOnly = NzbDrone.Core.Authentication.Policies.AdminOnly;
    public const string Operator = NzbDrone.Core.Authentication.Policies.Operator;
    public const string Reader = NzbDrone.Core.Authentication.Policies.Reader;
}
