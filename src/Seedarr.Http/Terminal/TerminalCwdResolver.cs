// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using NzbDrone.Core.Configuration;

namespace Seedarr.Http.Terminal;

public static class TerminalCwdResolver
{
    public static string ResolveWorkingDirectory(string requestedCwd, IConfigService configService)
    {
        var roots = GetAllowedRoots(configService);

        if (string.IsNullOrWhiteSpace(requestedCwd))
        {
            return GetDefaultWorkingDirectory(roots, configService);
        }

        return ValidateUnderAllowedRoots(requestedCwd, roots);
    }

    public static IReadOnlyList<string> GetAllowedRoots(IConfigService configService)
    {
        var roots = new List<string>();
        AddRootIfPresent(roots, configService?.TorrentSaveDirectory);
        AddRootIfPresent(roots, configService?.DefaultSavePath);

        if (roots.Count == 0)
        {
            AddRootIfPresent(roots, Directory.GetCurrentDirectory());
        }

        return roots;
    }

    public static bool IsPathUnderRoot(string fullPath, string rootFullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(rootFullPath))
        {
            return false;
        }

        var root = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSep = root + Path.DirectorySeparatorChar;

        return string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultWorkingDirectory(IReadOnlyList<string> roots, IConfigService configService)
    {
        if (!string.IsNullOrWhiteSpace(configService?.TorrentSaveDirectory))
        {
            return ValidateUnderAllowedRoots(configService.TorrentSaveDirectory, roots);
        }

        if (!string.IsNullOrWhiteSpace(configService?.DefaultSavePath))
        {
            return ValidateUnderAllowedRoots(configService.DefaultSavePath, roots);
        }

        return ValidateUnderAllowedRoots(roots[0], roots);
    }

    private static string ValidateUnderAllowedRoots(string cwd, IReadOnlyList<string> roots)
    {
        if (cwd.Contains('\0') || cwd.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException("Invalid working directory path specified.", nameof(cwd));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(cwd);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to resolve working directory: {ex.Message}", nameof(cwd), ex);
        }

        foreach (var root in roots)
        {
            if (IsPathUnderRoot(fullPath, root))
            {
                EnsureExistingDirectory(fullPath);
                return fullPath;
            }
        }

        throw new ArgumentException(
            "Working directory must be under configured torrent save paths.",
            nameof(cwd));
    }

    private static void AddRootIfPresent(List<string> roots, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var full = Path.GetFullPath(path);
            foreach (var existing in roots)
            {
                if (string.Equals(existing, full, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            roots.Add(full);
        }
        catch
        {
            // Ignore unresolvable configured roots; validation will use remaining roots.
        }
    }

    private static void EnsureExistingDirectory(string path)
    {
        if (File.Exists(path) && !Directory.Exists(path))
        {
            throw new ArgumentException("Working directory path refers to a file, not a directory.", nameof(path));
        }

        if (!Directory.Exists(path))
        {
            throw new ArgumentException("Working directory does not exist.", nameof(path));
        }
    }
}
