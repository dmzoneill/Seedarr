using System;
using System.Collections.Generic;
using System.IO;
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

    private static readonly HashSet<string> BlockedUnixPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/etc",
        "/proc",
        "/sys",
        "/dev",
        "/root",
        "/var/run",
        "/boot",
    };

    private static readonly string[] BlockedWindowsRootDirectoryNames =
    {
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
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

    public static bool IsBlockedPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (OperatingSystem.IsWindows())
        {
            return IsUnderBlockedWindowsRootDirectories(normalized);
        }

        foreach (var blocked in BlockedUnixPaths)
        {
            if (IsPathUnderBlockedRoot(normalized, blocked))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnderBlockedWindowsRootDirectories(string normalized)
    {
        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        foreach (var directoryName in BlockedWindowsRootDirectoryNames)
        {
            var blockedRoot = Path.Combine(root, directoryName);
            if (IsPathUnderBlockedRoot(normalized, blockedRoot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPathUnderBlockedRoot(string normalized, string blockedRoot)
    {
        var normalizedBlocked = blockedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalized.Equals(normalizedBlocked, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(normalizedBlocked + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(normalizedBlocked + "/", StringComparison.OrdinalIgnoreCase);
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

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (IsBlockedPath(fullPath))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var isUncRooted = path.Length >= 2 && path[0] == '\\' && path[1] == '\\';
        var isWindowsRooted = path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
        var isUnixRooted = path.StartsWith('/') || (path.StartsWith('\\') && !isUncRooted);

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

            var trimmed = segment.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == ".")
            {
                continue;
            }

            if (trimmed == "..")
            {
                var minCount = isWindowsRooted ? 1 : isUncRooted ? 2 : 0;
                if (sanitizedSegments.Count > minCount)
                {
                    sanitizedSegments.RemoveAt(sanitizedSegments.Count - 1);
                }

                continue;
            }

            var sanitized = SanitizeFileName(trimmed);
            if (!string.IsNullOrEmpty(sanitized))
            {
                sanitizedSegments.Add(sanitized);
            }
        }

        var separator = System.IO.Path.DirectorySeparatorChar.ToString();
        string result;
        if (isUncRooted)
        {
            if (sanitizedSegments.Count == 0)
            {
                return separator + separator;
            }

            if (sanitizedSegments.Count == 1)
            {
                return separator + separator + sanitizedSegments[0];
            }

            var uncTail = sanitizedSegments.Count > 2
                ? separator + string.Join(separator, sanitizedSegments.Skip(2))
                : string.Empty;
            result = separator + separator + sanitizedSegments[0] + separator + sanitizedSegments[1] + uncTail;
        }
        else
        {
            result = string.Join(separator, sanitizedSegments);
            if (isUnixRooted && !isWindowsRooted)
            {
                result = separator + result;
            }
        }

        return result;
    }

    public static string SanitizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == ".")
            {
                continue;
            }

            if (trimmed == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            var sanitized = SanitizeFileName(trimmed);
            if (!string.IsNullOrEmpty(sanitized))
            {
                stack.Add(sanitized);
            }
        }

        return string.Join("/", stack);
    }

    public static bool IsPathUnderRoot(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        if (ContainsPathTraversal(relativePath))
        {
            return false;
        }

        if (!System.IO.Path.IsPathFullyQualified(rootPath))
        {
            return false;
        }

        try
        {
            var fullRoot = System.IO.Path.GetFullPath(rootPath);
            var rootWithSep = fullRoot.EndsWith(System.IO.Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + System.IO.Path.DirectorySeparatorChar;

            var fullTarget = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relativePath));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return fullTarget.StartsWith(rootWithSep, comparison) ||
                   string.Equals(fullTarget, fullRoot, comparison);
        }
        catch
        {
            return false;
        }
    }
}
