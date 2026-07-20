namespace NzbDrone.Common.Extensions;

public static class StringExtensions
{
    public static bool IsNullOrWhiteSpace(this string text)
    {
        return string.IsNullOrWhiteSpace(text);
    }

    public static bool IsNotNullOrWhiteSpace(this string text)
    {
        return !string.IsNullOrWhiteSpace(text);
    }
}
