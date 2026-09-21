namespace NzbDrone.Core.Authentication;

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string Operator = "Operator";
    public const string Reader = "Reader";
}
