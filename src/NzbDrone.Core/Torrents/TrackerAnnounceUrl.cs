using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Torrents;

public static class TrackerAnnounceUrl
{
    public static bool IsAnnounceUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var clean = url.Trim();
        return clean.StartsWith("udp://", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    public static string FirstAnnounceUrl(IEnumerable<string> urls)
    {
        if (urls == null)
        {
            return null;
        }

        foreach (var url in urls)
        {
            if (IsAnnounceUrl(url))
            {
                return url.Trim();
            }
        }

        return null;
    }
}
