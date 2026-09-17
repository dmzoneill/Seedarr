using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Common.Disk;

public static class PathSanitizer
{
    private static readonly Regex WindowsReservedNameRegex = new(
        @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly char[] IllegalFileNameChars = new[]
    {
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    };

    public static bool IsWindowsReservedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return WindowsReservedNameRegex.IsMatch(name.Trim());
    }

    public static bool IsValidFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name == "." || name == "..")
        {
            return false;
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (char.IsControl(c) || IllegalFileNameChars.Contains(c))
            {
                return false;
            }
        }

        if (IsWindowsReservedName(name))
        {
            return false;
        }

        return true;
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (!char.IsControl(c) && !IllegalFileNameChars.Contains(c))
            {
                sb.Append(c);
            }
        }

        var sanitized = sb.ToString().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized == "." || sanitized == "..")
        {
            return string.Empty;
        }

        if (IsWindowsReservedName(sanitized))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    public static bool ContainsPathTraversal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("../", StringComparison.Ordinal) ||
            normalized.Contains("/..", StringComparison.Ordinal) ||
            normalized.EndsWith("/..", StringComparison.Ordinal) ||
            normalized == "..")
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var trimmedSegment = segment.Trim();
            if (trimmedSegment == ".." || trimmedSegment == ".")
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (ContainsPathTraversal(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];

            // Allow Windows drive specifier like "C:" as first segment
            if (i == 0 && segment.Length == 2 && char.IsLetter(segment[0]) && segment[1] == ':')
            {
                continue;
            }

            if (!IsValidFileName(segment))
            {
                return false;
            }
        }

        return true;
    }

    public static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var isWindowsRooted = path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
        var isUnixRooted = path.StartsWith('/') || path.StartsWith('\\');

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sanitizedSegments = new List<string>();

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (i == 0 && isWindowsRooted && segment.Length == 2 && char.IsLetter(segment[0]) && segment[1] == ':')
            {
                sanitizedSegments.Add(segment);
                continue;
            }

            if (segment == "." || segment == "..")
            {
                continue;
            }

            var sanitized = SanitizeFileName(segment);
            if (!string.IsNullOrEmpty(sanitized))
            {
                sanitizedSegments.Add(sanitized);
            }
        }

        var separator = System.IO.Path.DirectorySeparatorChar.ToString();
        var result = string.Join(separator, sanitizedSegments);
        if (isUnixRooted && !isWindowsRooted)
        {
            result = separator + result;
        }

        return result;
    }
}
